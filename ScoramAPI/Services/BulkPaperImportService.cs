using System.Globalization;
using System.Text.Json;
using ClosedXML.Excel;
using CsvHelper;
using Microsoft.EntityFrameworkCore;
using ScoramAPI.Data;
using ScoramAPI.DTOs;
using ScoramAPI.Enums;

namespace ScoramAPI.Services
{
    public interface IBulkPaperImportService
    {
        /// <summary>Reads a CSV, Excel (.xlsx), or JSON file into a common row shape. Columns/
        /// properties are matched case-insensitively: ExamName, Year, Medium are required; Tier,
        /// Shift, Date, PaperCode, PaperLabel are optional. Throws InvalidDataException with a
        /// human-readable message on structural problems (missing required columns, unreadable
        /// file/malformed JSON) -- anything that means "this file itself is unusable," as opposed to
        /// a single bad row, which ValidateAsync handles instead.</summary>
        Task<List<ImportedPaperRow>> ParseAsync(Stream fileStream, ImportFileFormat format);

        /// <summary>Annotates each row's IsValid/Errors/ExamExists/PaperAlreadyExists in place. A row
        /// is invalid if it's missing a required field, has an out-of-range Year, an unparseable
        /// Medium, an unparseable Date, or its full identity (Exam+Year+Medium+Tier+Shift+Date+Code+
        /// Label) collides with another row in this same file. A row whose identity instead collides
        /// with a paper that already exists in the database stays valid -- it's just flagged
        /// PaperAlreadyExists so Commit can skip it without treating the whole batch as an error.</summary>
        Task ValidateAsync(List<ImportedPaperRow> rows, ScoramDbContext db);
    }

    public class BulkPaperImportService : IBulkPaperImportService
    {
        private static readonly string[] RequiredHeaders = { "ExamName", "Year", "Medium" };

        public async Task<List<ImportedPaperRow>> ParseAsync(Stream fileStream, ImportFileFormat format)
        {
            return format switch
            {
                ImportFileFormat.Csv => await ParseCsvAsync(fileStream),
                ImportFileFormat.Excel => ParseExcel(fileStream),
                ImportFileFormat.Json => await ParseJsonAsync(fileStream),
                _ => throw new InvalidDataException("Unsupported file format -- expected .csv, .xlsx, or .json.")
            };
        }

        private async Task<List<ImportedPaperRow>> ParseCsvAsync(Stream fileStream)
        {
            using var reader = new StreamReader(fileStream);
            using var csv = new CsvReader(reader, CultureInfo.InvariantCulture);

            if (!await csv.ReadAsync() || !csv.ReadHeader())
                throw new InvalidDataException("The CSV file appears to be empty.");

            CheckHeaders(csv.HeaderRecord ?? Array.Empty<string>());

            var rows = new List<ImportedPaperRow>();
            var rowNumber = 0;
            while (await csv.ReadAsync())
            {
                rowNumber++;
                rows.Add(MapRow(rowNumber, header => csv.GetField(header) ?? string.Empty));
            }
            return rows;
        }

        private List<ImportedPaperRow> ParseExcel(Stream fileStream)
        {
            using var workbook = new XLWorkbook(fileStream);
            var sheet = workbook.Worksheets.FirstOrDefault()
                ?? throw new InvalidDataException("The Excel file has no worksheets.");

            // RowsUsed() already skips fully-blank rows, so gaps in the data don't need special
            // handling here the way a fixed row-range scan would.
            var usedRows = sheet.RowsUsed().ToList();
            if (usedRows.Count == 0)
                throw new InvalidDataException("The Excel file appears to be empty.");

            var headerRow = usedRows[0];
            const int maxColumnsToScan = 15; // our expected header set has 8 columns
            var columnIndexByHeader = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            for (var col = 1; col <= maxColumnsToScan; col++)
            {
                var headerText = headerRow.Cell(col).GetString().Trim();
                if (string.IsNullOrEmpty(headerText)) continue;
                if (!columnIndexByHeader.ContainsKey(headerText))
                    columnIndexByHeader[headerText] = col;
            }
            CheckHeaders(columnIndexByHeader.Keys);

            var rows = new List<ImportedPaperRow>();
            var rowNumber = 0;
            foreach (var excelRow in usedRows.Skip(1))
            {
                rowNumber++;
                rows.Add(MapRow(rowNumber, header =>
                    columnIndexByHeader.TryGetValue(header, out var col) ? excelRow.Cell(col).GetString().Trim() : string.Empty));
            }
            return rows;
        }

        // Same JSON shape/case-insensitive-property pattern as BulkImportService.ParseJsonAsync
        // (the question-bulk-import equivalent) -- a top-level array of objects, one per paper.
        private async Task<List<ImportedPaperRow>> ParseJsonAsync(Stream fileStream)
        {
            List<JsonPaperRow>? parsed;
            try
            {
                parsed = await JsonSerializer.DeserializeAsync<List<JsonPaperRow>>(fileStream,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            }
            catch (JsonException ex)
            {
                throw new InvalidDataException($"That doesn't look like valid JSON: {ex.Message}");
            }

            if (parsed == null || parsed.Count == 0)
                throw new InvalidDataException("The JSON file contains no papers (expected a top-level array).");

            var rows = new List<ImportedPaperRow>();
            for (var i = 0; i < parsed.Count; i++)
            {
                var j = parsed[i];
                // Routed through the exact same MapRow used by ParseCsvAsync/ParseExcel below, via a
                // header-name lookup function, so every format shares one place that decides how a
                // raw field becomes an ImportedPaperRow (date parsing, blank-to-null, trimming, etc.)
                // -- a JSON-only bug in that logic simply can't happen.
                rows.Add(MapRow(i + 1, header => header switch
                {
                    "ExamName" => j.ExamName ?? "",
                    "Year" => j.Year?.ToString() ?? "",
                    "Tier" => j.Tier ?? "",
                    "Shift" => j.Shift ?? "",
                    "Date" => j.Date ?? "",
                    "Medium" => j.Medium ?? "",
                    "PaperCode" => j.PaperCode ?? "",
                    "PaperLabel" => j.PaperLabel ?? "",
                    _ => ""
                }));
            }
            return rows;
        }

        private class JsonPaperRow
        {
            public string? ExamName { get; set; }
            public int? Year { get; set; }
            public string? Tier { get; set; }
            public string? Shift { get; set; }
            public string? Date { get; set; }
            public string? Medium { get; set; }
            public string? PaperCode { get; set; }
            public string? PaperLabel { get; set; }
        }

        private static void CheckHeaders(IEnumerable<string> actualHeaders)
        {
            var present = new HashSet<string>(actualHeaders, StringComparer.OrdinalIgnoreCase);
            var missing = RequiredHeaders.Where(h => !present.Contains(h)).ToList();
            if (missing.Count > 0)
                throw new InvalidDataException($"Missing required column(s): {string.Join(", ", missing)}. " +
                    "Expected headers: ExamName, Year, Medium (Tier, Shift, Date, PaperCode, PaperLabel are optional).");
        }

        // Shared row-building logic for all three formats. Date is parsed strictly as YYYY-MM-DD -- the
        // one unambiguous format regardless of the admin's locale -- rather than a locale-guessing
        // parse that could silently swap day and month.
        private static ImportedPaperRow MapRow(int rowNumber, Func<string, string> field)
        {
            _ = int.TryParse(field("Year").Trim(), out var year);
            var dateRaw = field("Date").Trim();
            var examDate = DateOnly.TryParseExact(dateRaw, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)
                ? parsed
                : (DateOnly?)null;

            return new ImportedPaperRow
            {
                RowNumber = rowNumber,
                ExamName = field("ExamName").Trim(),
                Year = year,
                Tier = NullIfBlank(field("Tier")),
                Shift = NullIfBlank(field("Shift")),
                ExamDateRaw = dateRaw,
                ExamDate = examDate,
                Medium = field("Medium").Trim(),
                PaperCode = NullIfBlank(field("PaperCode")),
                PaperLabel = NullIfBlank(field("PaperLabel"))
            };
        }

        private static string? NullIfBlank(string s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

        public async Task ValidateAsync(List<ImportedPaperRow> rows, ScoramDbContext db)
        {
            // Name -> Id, built once and matched case-insensitively in memory -- avoids both a
            // second per-row DB query below and any dependence on the database's own collation
            // (which might or might not be case-insensitive) for what should always be a
            // case-insensitive exam-name match, consistent with examNameSet above.
            var examIdsByName = await db.Exams.ToDictionaryAsync(e => e.Name, e => e.Id, StringComparer.OrdinalIgnoreCase);

            // Batch-internal duplicate detection, keyed on the row's full identity tuple (same fields
            // as the DB duplicate check below) -- two rows naming the exact same not-yet-created exam
            // collide with each other exactly like two rows naming an existing one would.
            var seenInBatch = new HashSet<string>();

            foreach (var row in rows)
            {
                row.Errors = new List<string>();

                if (string.IsNullOrWhiteSpace(row.ExamName)) row.Errors.Add("Exam name is required.");
                if (row.Year < 1990 || row.Year > DateTime.UtcNow.Year + 1)
                    row.Errors.Add($"Year must be between 1990 and {DateTime.UtcNow.Year + 1}.");

                if (string.IsNullOrWhiteSpace(row.Medium))
                    row.Errors.Add("Medium is required (Hindi or English).");
                else if (!Enum.TryParse<PaperLanguage>(row.Medium, ignoreCase: true, out _))
                    row.Errors.Add($"'{row.Medium}' isn't a valid medium (expected Hindi or English).");

                if (!string.IsNullOrWhiteSpace(row.ExamDateRaw) && row.ExamDate == null)
                    row.Errors.Add($"'{row.ExamDateRaw}' isn't a valid date (expected YYYY-MM-DD).");

                if (row.Errors.Count > 0)
                {
                    row.IsValid = false;
                    continue;
                }

                row.ExamExists = examIdsByName.ContainsKey(row.ExamName.Trim());

                var identityKey = string.Join("|",
                    row.ExamName.Trim().ToLowerInvariant(), row.Year, row.Medium.Trim().ToLowerInvariant(),
                    row.Tier?.ToLowerInvariant() ?? "", row.Shift?.ToLowerInvariant() ?? "",
                    row.ExamDate?.ToString("yyyy-MM-dd") ?? "", row.PaperCode?.ToLowerInvariant() ?? "",
                    row.PaperLabel?.ToLowerInvariant() ?? "");

                if (!seenInBatch.Add(identityKey))
                {
                    row.Errors.Add("This exact paper (same exam, year, medium, tier, shift, date, code, and label) is duplicated elsewhere in this file.");
                    row.IsValid = false;
                    continue;
                }

                row.IsValid = true;

                if (row.ExamExists)
                {
                    var examId = examIdsByName[row.ExamName.Trim()];
                    var language = Enum.Parse<PaperLanguage>(row.Medium, ignoreCase: true);
                    row.PaperAlreadyExists = await db.Papers.AnyAsync(p =>
                        p.ExamId == examId && p.Year == row.Year && p.Language == language &&
                        p.PaperCode == row.PaperCode && p.Tier == row.Tier &&
                        p.ExamDate == row.ExamDate && p.Shift == row.Shift && p.PaperLabel == row.PaperLabel);
                }
                // If the exam doesn't exist yet, no paper referencing it can possibly exist yet
                // either -- PaperAlreadyExists correctly stays false without a query.
            }
        }
    }
}
