using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ScoramAPI.Data;
using ScoramAPI.DTOs;
using ScoramAPI.Enums;
using ScoramAPI.Services;

namespace ScoramAPI.Controllers
{
    // One endpoint, one round trip -- the dashboard fires this once on load rather than the previous
    // approach of the frontend stitching together listExams()+listMyTasks() client-side. Open to any
    // authenticated admin (no permission gate): every number here is either already visible elsewhere
    // in the admin panel to any admin, or is harmless aggregate/operational info.
    [ApiController]
    [Route("api/admin/dashboard")]
    [Authorize(Roles = "Admin,SuperAdmin")]
    public class DashboardController : ControllerBase
    {
        private readonly ScoramDbContext _db;
        private readonly IInstantSearchService _instantSearch;
        private readonly IAdminPermissionService _permissions;

        public DashboardController(ScoramDbContext db, IInstantSearchService instantSearch, IAdminPermissionService permissions)
        {
            _db = db;
            _instantSearch = instantSearch;
            _permissions = permissions;
        }

        [HttpGet("stats")]
        public async Task<ActionResult<DashboardStatsDto>> GetStats()
        {
            var today = DateTime.UtcNow.Date;

            var content = new DashboardContentStatsDto
            {
                TotalQuestions = await _db.Questions.CountAsync(),
                TotalPapers = await _db.Papers.CountAsync(),
                TotalExams = await _db.Exams.CountAsync(),
                PublishedPapers = await _db.Papers.CountAsync(p => p.Status == PaperStatus.Published),
                DraftPapers = await _db.Papers.CountAsync(p => p.Status == PaperStatus.Draft),
                PendingReviewPapers = await _db.Papers.CountAsync(p => p.Status == PaperStatus.PendingReview),
                TotalMockTests = await _db.MockTests.CountAsync()
            };

            var activity = new DashboardActivityStatsDto
            {
                TodayUploads = await _db.Papers.CountAsync(p => p.CreatedAt >= today),
                TodayActiveUsers = await _db.Users.CountAsync(u => u.LastActiveAt != null && u.LastActiveAt >= today),
                PendingQuestionReports = await _db.QuestionReports.CountAsync(r => r.Status == ReportStatus.Pending),
                PendingChatReports = await _db.ChatReports.CountAsync(r => r.Status == ChatReportStatus.Pending)
            };

            var system = new DashboardSystemStatusDto
            {
                DatabaseHealthy = await TryCheckDatabaseAsync(),
                SearchIndexHealthy = await _instantSearch.IsHealthyAsync()
            };

            var dailyUploads = await GetDailyUploadsAsync(today);
            var monthlyUploads = await GetMonthlyUploadsAsync(today);
            var canViewAudit = await _permissions.HasPermissionAsync(User, AdminPermission.Audit);
            var recentActivity = canViewAudit ? await GetRecentActivityAsync() : new List<AuditLogResponseDto>();
            var latestUploads = await GetLatestUploadsAsync();
            var adminPerformance = await GetAdminPerformanceAsync();

            return Ok(new DashboardStatsDto
            {
                Content = content,
                Activity = activity,
                System = system,
                DailyUploads = dailyUploads,
                MonthlyUploads = monthlyUploads,
                RecentActivity = recentActivity,
                LatestUploads = latestUploads,
                AdminPerformance = adminPerformance
            });
        }

        private async Task<bool> TryCheckDatabaseAsync()
        {
            try
            {
                return await _db.Database.CanConnectAsync();
            }
            catch
            {
                return false;
            }
        }

        // Last 7 days including today, oldest first -- what a "weekly graph" of uploads means here.
        private async Task<List<DashboardGraphPointDto>> GetDailyUploadsAsync(DateTime today)
        {
            var startDate = today.AddDays(-6);
            var counts = await _db.Papers
                .Where(p => p.CreatedAt >= startDate)
                .GroupBy(p => p.CreatedAt.Date)
                .Select(g => new { Date = g.Key, Count = g.Count() })
                .ToListAsync();

            var result = new List<DashboardGraphPointDto>();
            for (var date = startDate; date <= today; date = date.AddDays(1))
            {
                var match = counts.FirstOrDefault(c => c.Date == date);
                result.Add(new DashboardGraphPointDto { Label = date.ToString("ddd d MMM"), Count = match?.Count ?? 0 });
            }
            return result;
        }

        // Last 6 months including the current one, oldest first.
        private async Task<List<DashboardGraphPointDto>> GetMonthlyUploadsAsync(DateTime today)
        {
            var startMonth = new DateTime(today.Year, today.Month, 1).AddMonths(-5);
            var counts = await _db.Papers
                .Where(p => p.CreatedAt >= startMonth)
                .GroupBy(p => new { p.CreatedAt.Year, p.CreatedAt.Month })
                .Select(g => new { g.Key.Year, g.Key.Month, Count = g.Count() })
                .ToListAsync();

            var result = new List<DashboardGraphPointDto>();
            for (var month = startMonth; month <= today; month = month.AddMonths(1))
            {
                var match = counts.FirstOrDefault(c => c.Year == month.Year && c.Month == month.Month);
                result.Add(new DashboardGraphPointDto { Label = month.ToString("MMM yyyy"), Count = match?.Count ?? 0 });
            }
            return result;
        }

        private async Task<List<AuditLogResponseDto>> GetRecentActivityAsync()
        {
            return await _db.AuditLogs
                .Include(a => a.Admin)
                .OrderByDescending(a => a.CreatedAt)
                .Take(8)
                .Select(a => new AuditLogResponseDto
                {
                    Id = a.Id,
                    AdminId = a.AdminId,
                    AdminName = a.Admin != null ? a.Admin.FullName : "Unknown",
                    Action = a.Action,
                    TargetType = a.TargetType,
                    TargetId = a.TargetId,
                    Detail = a.Detail,
                    CreatedAt = a.CreatedAt
                })
                .ToListAsync();
        }

        private async Task<List<DashboardLatestPaperDto>> GetLatestUploadsAsync()
        {
            return await _db.Papers
                .Include(p => p.Exam)
                .OrderByDescending(p => p.CreatedAt)
                .Take(6)
                .Select(p => new DashboardLatestPaperDto
                {
                    Id = p.Id,
                    ExamName = p.Exam != null ? p.Exam.Name : "Unknown",
                    Year = p.Year,
                    Language = p.Language.ToString(),
                    Status = p.Status.ToString(),
                    QuestionCount = p.Questions.Count,
                    CreatedAt = p.CreatedAt
                })
                .ToListAsync();
        }

        // Top 5 admins by papers uploaded, all-time -- "Admin Performance" per the product spec.
        // Intentionally simple (upload/publish counts only, no scoring/ranking weight) -- this is meant
        // to give a Super Admin a quick read on who's actively contributing, not to gamify or rank
        // admins against each other.
        private async Task<List<DashboardAdminPerformanceDto>> GetAdminPerformanceAsync()
        {
            return await _db.Admins
                .Where(a => a.UploadedPapers.Count > 0)
                .OrderByDescending(a => a.UploadedPapers.Count)
                .Take(5)
                .Select(a => new DashboardAdminPerformanceDto
                {
                    AdminId = a.Id,
                    FullName = a.FullName,
                    PapersUploaded = a.UploadedPapers.Count,
                    PapersPublished = a.UploadedPapers.Count(p => p.Status == PaperStatus.Published)
                })
                .ToListAsync();
        }
    }
}
