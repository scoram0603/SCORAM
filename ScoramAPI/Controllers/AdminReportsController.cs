using System.Globalization;
using System.Text;
using ClosedXML.Excel;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ScoramAPI.Data;
using ScoramAPI.Enums;
using ScoramAPI.Extensions;
using ScoramAPI.Services;

namespace ScoramAPI.Controllers
{
    // REPORTS & EXPORTS -- date-filtered CSV / Excel downloads.
    //
    // Rules:
    //   * Every export needs ExportReports. Row-level reports ALSO need the permission that opens the same
    //     data in the panel (ManageUsers for students, Audit for the audit trail), so exporting never
    //     widens what an admin can see.
    //   * Aggregated "daily" reports reuse AdminAnalyticsService, so exported numbers are the same numbers
    //     the dashboard and charts show.
    //   * Data minimisation: the student report contains username, signup date, status and activity
    //     timestamps only -- no names, emails, phone numbers or password data.
    //   * Capped at MaxRows; a truncated file is flagged with the X-Report-Truncated header and a note row.
    //   * Cells that start with = + - @ are prefixed with ' so spreadsheets never run them as formulas.
    //   * Every export is written to the audit log (type, range, format, row count -- never the data).
    [ApiController]
    [Route("api/admin/reports")]
    [Authorize(Roles = "Admin,SuperAdmin")]
    public class AdminReportsController : ControllerBase
    {
        private const int MaxRows = 50_000;

        private sealed class ReportTable
        {
            public string Title { get; init; } = string.Empty;
            public string[] Headers { get; init; } = Array.Empty<string>();
            public List<object?[]> Rows { get; init; } = new();
            public bool Truncated { get; set; }
            public string Definition { get; init; } = string.Empty;
        }

        private sealed record ReportDef(string Key, string Label, string Description, string Kind, params AdminPermission[] Needs);

        private static readonly ReportDef[] Catalog =
        {
            new("daily-users", "Users - daily", "New signups, cumulative users, active learners and test attempts per day.", "Aggregated", AdminPermission.ViewAnalytics),
            new("daily-content", "Content - daily", "Papers uploaded, published and rejected per day.", "Aggregated", AdminPermission.ViewAnalytics),
            new("daily-learning", "Learning - daily", "Test attempts started, completed, abandoned and in progress per day.", "Aggregated", AdminPermission.ViewAnalytics),
            new("daily-moderation", "Moderation - daily", "Question reports filed and resolved per day.", "Aggregated", AdminPermission.ViewAnalytics),
            new("papers", "Papers created", "One row per paper created in the period, with exam, status and dates.", "Row-level", AdminPermission.ViewAnalytics),
            new("students", "Student signups (minimal)", "One row per student account created in the period: username, status and activity dates only. No contact details.", "Row-level", AdminPermission.ManageUsers),
            new("audit", "Admin audit trail", "One row per audited admin action in the period.", "Row-level", AdminPermission.Audit),
        };

        private readonly ScoramDbContext _db;
        private readonly IAdminPermissionService _permissions;
        private readonly IAdminAnalyticsService _analytics;
        private readonly IAuditLogService _audit;

        public AdminReportsController(ScoramDbContext db, IAdminPermissionService permissions,
            IAdminAnalyticsService analytics, IAuditLogService audit)
        {
            _db = db;
            _permissions = permissions;
            _analytics = analytics;
            _audit = audit;
        }

        private async Task<bool> CanAsync(ReportDef def)
        {
            if (!await _permissions.HasPermissionAsync(User, AdminPermission.ExportReports)) return false;
            foreach (var need in def.Needs)
                if (!await _permissions.HasPermissionAsync(User, need)) return false;
            return true;
        }

        // GET /api/admin/reports/catalog -- only the reports THIS admin may download.
        [HttpGet("catalog")]
        public async Task<IActionResult> GetCatalog()
        {
            var items = new List<object>();
            foreach (var def in Catalog)
            {
                if (await CanAsync(def))
                    items.Add(new { key = def.Key, label = def.Label, description = def.Description, kind = def.Kind });
            }
            return Ok(items);
        }

        // GET /api/admin/reports/{type}?from=&to=&tzOffsetMinutes=330&format=csv|xlsx
        [HttpGet("{type}")]
        public async Task<IActionResult> Download(string type,
            [FromQuery] string? from, [FromQuery] string? to, [FromQuery] int? tzOffsetMinutes = null,
            [FromQuery] string format = "csv", CancellationToken ct = default)
        {
            var def = Catalog.FirstOrDefault(d => d.Key == type);
            if (def == null) return NotFound(new { message = "Unknown report." });
            if (!await CanAsync(def)) return Forbid();

            format = (format ?? "csv").Trim().ToLowerInvariant();
            if (format != "csv" && format != "xlsx") return BadRequest(new { message = "format must be csv or xlsx." });

            // Exports always use daily buckets.
            var range = _analytics.ResolveRange(from, to, tzOffsetMinutes, "day");
            var table = await BuildAsync(def.Key, range, ct);

            var stamp = $"{range.FromLocal:yyyyMMdd}-{range.ToLocal:yyyyMMdd}";
            var fileBase = $"scoram-{def.Key}-{stamp}";

            await _audit.LogAsync(User.GetAdminId(), "Report.Export", "Report", null,
                $"{def.Key} {range.FromLocal:yyyy-MM-dd}..{range.ToLocal:yyyy-MM-dd} {format}, {table.Rows.Count} rows{(table.Truncated ? " (truncated)" : "")}");

            if (table.Truncated) Response.Headers["X-Report-Truncated"] = "true";

            if (format == "xlsx")
            {
                var bytes = BuildXlsx(table, range, def);
                return File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", fileBase + ".xlsx");
            }

            return File(BuildCsv(table), "text/csv; charset=utf-8", fileBase + ".csv");
        }

        // ---------------------------------------------------------------------------------------------
        // Report builders
        // ---------------------------------------------------------------------------------------------

        private async Task<ReportTable> BuildAsync(string key, AnalyticsRange r, CancellationToken ct)
        {
            switch (key)
            {
                case "daily-users":
                {
                    var d = await _analytics.GetUsersAsync(r, ct);
                    return new ReportTable
                    {
                        Title = "Users - daily",
                        Definition = "Active learners = distinct students who started a test that day. Cumulative users = accounts created up to the end of that day.",
                        Headers = new[] { "Date", "New signups", "Cumulative users", "Active learners", "Test attempts started" },
                        Rows = d.Series.Select(x => new object?[] { x.Start, x.NewUsers, x.CumulativeUsers, x.ActiveLearners, x.TestAttempts }).ToList()
                    };
                }
                case "daily-content":
                {
                    var d = await _analytics.GetContentAsync(r, ct);
                    return new ReportTable
                    {
                        Title = "Content - daily",
                        Definition = "Uploaded = paper created; Published = paper publish time; Rejected = audited Paper.Reject actions.",
                        Headers = new[] { "Date", "Papers uploaded", "Papers published", "Papers rejected" },
                        Rows = d.Publishing.Select(x => new object?[] { x.Start, x.Uploaded, x.Published, x.Rejected }).ToList()
                    };
                }
                case "daily-learning":
                {
                    var d = await _analytics.GetLearningAsync(r, ct);
                    return new ReportTable
                    {
                        Title = "Learning - daily",
                        Definition = "Attempts grouped by the day they were started. Completed = submitted or auto-submitted; Abandoned = expired; In progress = still open.",
                        Headers = new[] { "Date", "Started", "Completed", "Abandoned", "In progress" },
                        Rows = d.Activity.Select(x => new object?[] { x.Start, x.Started, x.Completed, x.Abandoned, x.InProgress }).ToList()
                    };
                }
                case "daily-moderation":
                {
                    var d = await _analytics.GetCommunityAsync(r, ct);
                    return new ReportTable
                    {
                        Title = "Moderation - daily",
                        Definition = "Reported = question reports filed that day; Resolved = question reports closed that day (any filing date).",
                        Headers = new[] { "Date", "Question reports filed", "Question reports resolved" },
                        Rows = d.QuestionReportsTrend.Select(x => new object?[] { x.Start, x.Reported, x.Resolved }).ToList()
                    };
                }
                case "papers":
                {
                    var rows = await _db.Papers
                        .Where(p => p.CreatedAt >= r.FromUtc && p.CreatedAt < r.ToUtc)
                        .OrderBy(p => p.CreatedAt)
                        .Select(p => new
                        {
                            Exam = p.Exam != null ? p.Exam.Name : "",
                            p.Year, p.Language, p.Status, p.PaperCode, p.Tier, p.Shift,
                            p.CreatedAt, p.PublishedAt,
                            Questions = p.Questions.Count + p.QuestionBankLinks.Count,
                            Uploader = p.CreatedByAdmin != null ? p.CreatedByAdmin.FullName : ""
                        })
                        .Take(MaxRows + 1)
                        .ToListAsync(ct);
                    var truncated = rows.Count > MaxRows;
                    return new ReportTable
                    {
                        Title = "Papers created",
                        Truncated = truncated,
                        Definition = "Papers created in the selected period (admin-local dates). Questions = PYP questions plus Question Bank questions linked to the paper.",
                        Headers = new[] { "Exam", "Year", "Language", "Status", "Paper code", "Tier", "Shift", "Created", "Published", "Questions", "Uploaded by" },
                        Rows = rows.Take(MaxRows).Select(p => new object?[]
                        {
                            p.Exam, p.Year, p.Language.ToString(), p.Status.ToString(), p.PaperCode, p.Tier, p.Shift,
                            Local(p.CreatedAt, r), Local(p.PublishedAt, r), p.Questions, p.Uploader
                        }).ToList()
                    };
                }
                case "students":
                {
                    var rows = await _db.Users
                        .Where(u => u.CreatedAt >= r.FromUtc && u.CreatedAt < r.ToUtc)
                        .OrderBy(u => u.CreatedAt)
                        .Select(u => new { u.Username, u.CreatedAt, u.IsActive, u.PhoneVerified, u.LastActiveAt })
                        .Take(MaxRows + 1)
                        .ToListAsync(ct);
                    var truncated = rows.Count > MaxRows;
                    return new ReportTable
                    {
                        Title = "Student signups (minimal)",
                        Truncated = truncated,
                        Definition = "Students created in the selected period. Contains no names, emails or phone numbers. Last login is the most recent login only.",
                        Headers = new[] { "Username", "Signed up", "Status", "Phone verified", "Last login" },
                        Rows = rows.Take(MaxRows).Select(u => new object?[]
                        {
                            u.Username, Local(u.CreatedAt, r), u.IsActive ? "Active" : "Suspended", u.PhoneVerified ? "Yes" : "No", Local(u.LastActiveAt, r)
                        }).ToList()
                    };
                }
                case "audit":
                {
                    var rows = await _db.AuditLogs
                        .Where(a => a.CreatedAt >= r.FromUtc && a.CreatedAt < r.ToUtc)
                        .OrderBy(a => a.CreatedAt)
                        .Select(a => new
                        {
                            a.CreatedAt,
                            Admin = a.Admin != null ? a.Admin.FullName : "Unknown",
                            a.Action, a.TargetType, a.TargetId, a.Detail
                        })
                        .Take(MaxRows + 1)
                        .ToListAsync(ct);
                    var truncated = rows.Count > MaxRows;
                    return new ReportTable
                    {
                        Title = "Admin audit trail",
                        Truncated = truncated,
                        Definition = "Audited admin actions in the selected period. Credentials and tokens are never written to the audit log.",
                        Headers = new[] { "When", "Admin", "Action", "Target type", "Target id", "Detail" },
                        Rows = rows.Take(MaxRows).Select(a => new object?[]
                        {
                            Local(a.CreatedAt, r), a.Admin, a.Action, a.TargetType, a.TargetId?.ToString(), a.Detail
                        }).ToList()
                    };
                }
                default:
                    throw new InvalidOperationException("Unhandled report type.");
            }
        }

        private static string? Local(DateTime? utc, AnalyticsRange r) =>
            utc == null ? null : utc.Value.AddMinutes(r.OffsetMinutes).ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);

        // ---------------------------------------------------------------------------------------------
        // Writers
        // ---------------------------------------------------------------------------------------------

        // Neutralise spreadsheet formula injection: a text cell starting with = + - @ (or tab / CR) is
        // prefixed with a single quote, the standard mitigation.
        private static string SafeText(string? value)
        {
            if (string.IsNullOrEmpty(value)) return string.Empty;
            var first = value[0];
            return first == '=' || first == '+' || first == '-' || first == '@' || first == '\t' || first == '\r'
                ? "'" + value
                : value;
        }

        private static string Format(object? cell) => cell switch
        {
            null => string.Empty,
            string s => SafeText(s),
            IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
            _ => SafeText(cell.ToString())
        };

        private static string CsvEscape(string value)
        {
            if (value.IndexOfAny(new[] { ',', '"', '\r', '\n' }) >= 0)
                return "\"" + value.Replace("\"", "\"\"") + "\"";
            return value;
        }

        private static byte[] BuildCsv(ReportTable table)
        {
            var sb = new StringBuilder();
            sb.Append(string.Join(",", table.Headers.Select(h => CsvEscape(h)))).Append("\r\n");
            foreach (var row in table.Rows)
                sb.Append(string.Join(",", row.Select(c => CsvEscape(Format(c))))).Append("\r\n");
            if (table.Truncated)
                sb.Append(CsvEscape($"NOTE: output limited to the first {MaxRows} rows. Narrow the date range for the rest.")).Append("\r\n");

            // UTF-8 with BOM so Excel opens non-ASCII text (Hindi) correctly.
            var preamble = new UTF8Encoding(encoderShouldEmitUTF8Identifier: true).GetPreamble();
            var body = Encoding.UTF8.GetBytes(sb.ToString());
            var result = new byte[preamble.Length + body.Length];
            Buffer.BlockCopy(preamble, 0, result, 0, preamble.Length);
            Buffer.BlockCopy(body, 0, result, preamble.Length, body.Length);
            return result;
        }

        private static byte[] BuildXlsx(ReportTable table, AnalyticsRange r, ReportDef def)
        {
            using var workbook = new XLWorkbook();
            var sheet = workbook.Worksheets.Add("Report");

            for (var c = 0; c < table.Headers.Length; c++)
            {
                var cell = sheet.Cell(1, c + 1);
                cell.Value = table.Headers[c];
                cell.Style.Font.Bold = true;
            }

            for (var i = 0; i < table.Rows.Count; i++)
            {
                var row = table.Rows[i];
                for (var c = 0; c < row.Length; c++)
                {
                    var cell = sheet.Cell(i + 2, c + 1);
                    switch (row[c])
                    {
                        case null: break;
                        case int n: cell.Value = n; break;
                        case long l: cell.Value = l; break;
                        case double d: cell.Value = d; break;
                        case decimal m: cell.Value = m; break;
                        default: cell.Value = SafeText(row[c]!.ToString()); break;
                    }
                }
            }

            sheet.SheetView.FreezeRows(1);
            sheet.Columns().AdjustToContents();

            var about = workbook.Worksheets.Add("About");
            about.Cell(1, 1).Value = table.Title;
            about.Cell(1, 1).Style.Font.Bold = true;
            about.Cell(2, 1).Value = "Period (admin-local dates)";
            about.Cell(2, 2).Value = $"{r.FromLocal:yyyy-MM-dd} to {r.ToLocal:yyyy-MM-dd}";
            about.Cell(3, 1).Value = "Timezone offset (minutes from UTC)";
            about.Cell(3, 2).Value = r.OffsetMinutes;
            about.Cell(4, 1).Value = "Generated (UTC)";
            about.Cell(4, 2).Value = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
            about.Cell(5, 1).Value = "Definition";
            about.Cell(5, 2).Value = table.Definition;
            if (table.Truncated)
            {
                about.Cell(6, 1).Value = "Note";
                about.Cell(6, 2).Value = $"Output limited to the first {MaxRows} rows. Narrow the date range for the rest.";
            }
            about.Columns().AdjustToContents();

            using var stream = new MemoryStream();
            workbook.SaveAs(stream);
            return stream.ToArray();
        }
    }
}
