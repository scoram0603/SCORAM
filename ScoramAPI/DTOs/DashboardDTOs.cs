namespace ScoramAPI.DTOs
{
    public class DashboardStatsDto
    {
        public DashboardContentStatsDto Content { get; set; } = new();
        public DashboardActivityStatsDto Activity { get; set; } = new();
        public DashboardSystemStatusDto System { get; set; } = new();
        public List<DashboardGraphPointDto> DailyUploads { get; set; } = new();
        public List<DashboardGraphPointDto> MonthlyUploads { get; set; } = new();
        public List<AuditLogResponseDto> RecentActivity { get; set; } = new();
        public List<DashboardLatestPaperDto> LatestUploads { get; set; } = new();
        public List<DashboardAdminPerformanceDto> AdminPerformance { get; set; } = new();
    }

    public class DashboardContentStatsDto
    {
        public int TotalQuestions { get; set; }
        public int TotalPapers { get; set; }
        public int TotalExams { get; set; }
        public int PublishedPapers { get; set; }
        public int DraftPapers { get; set; }
        public int PendingReviewPapers { get; set; }
        public int TotalMockTests { get; set; }
    }

    public class DashboardActivityStatsDto
    {
        public int TodayUploads { get; set; }
        public int TodayActiveUsers { get; set; }
        public int PendingQuestionReports { get; set; }
        public int PendingChatReports { get; set; }
    }

    // Deliberately just three simple, honest signals -- not a fake "99.9% uptime" number. Each one
    // answers a question an admin would actually ask when something feels off: "is the DB up",
    // "is search up", "are we anywhere near running out of disk for uploads".
    public class DashboardSystemStatusDto
    {
        public bool DatabaseHealthy { get; set; }
        public bool SearchIndexHealthy { get; set; }
        public double StorageUsedMb { get; set; }
    }

    public class DashboardGraphPointDto
    {
        public string Label { get; set; } = string.Empty; // e.g. "Mon 28" or "Jul 2026"
        public int Count { get; set; }
    }

    public class DashboardLatestPaperDto
    {
        public Guid Id { get; set; }
        public string ExamName { get; set; } = string.Empty;
        public int Year { get; set; }
        public string Language { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public int QuestionCount { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    public class DashboardAdminPerformanceDto
    {
        public Guid AdminId { get; set; }
        public string FullName { get; set; } = string.Empty;
        public int PapersUploaded { get; set; }
        public int PapersPublished { get; set; }
    }
}
