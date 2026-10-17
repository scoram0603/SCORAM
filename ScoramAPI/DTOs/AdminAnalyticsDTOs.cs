namespace ScoramAPI.DTOs
{
    // ADMIN ANALYTICS -- response shapes for /api/admin/analytics/*. Every number here comes from a real
    // database aggregate; where something cannot be measured with the data SCORAM stores today, the item
    // appears in an "unavailable" list with the reason instead of being faked or omitted silently.

    public class AnalyticsRangeDto
    {
        public string From { get; set; } = string.Empty;          // yyyy-MM-dd, admin-local date, inclusive
        public string To { get; set; } = string.Empty;            // yyyy-MM-dd, admin-local date, inclusive
        public string PreviousFrom { get; set; } = string.Empty;
        public string PreviousTo { get; set; } = string.Empty;
        public int Days { get; set; }
        public string Granularity { get; set; } = "day";
        public int TzOffsetMinutes { get; set; }
        public DateTime GeneratedAt { get; set; }
    }

    public class KpiDto
    {
        public string Key { get; set; } = string.Empty;
        public string Group { get; set; } = string.Empty;
        public string Label { get; set; } = string.Empty;
        // Plain-language definition shown in the tooltip -- one source of truth shared with exports.
        public string Definition { get; set; } = string.Empty;
        // count | percent | minutes
        public string Unit { get; set; } = "count";
        // Admin route a click on the card drills into (null = no meaningful destination).
        public string? DrillTo { get; set; }
        public decimal? Value { get; set; }
        // Same metric over the immediately preceding period of equal length. Null for point-in-time
        // snapshots (e.g. "total registered users"), where a "previous period" would be fabricated.
        public decimal? Previous { get; set; }
        public decimal? Delta { get; set; }
        public decimal? DeltaPct { get; set; }
        // up_good | down_good | neutral -- decides whether an increase is drawn green or red.
        public string Polarity { get; set; } = "neutral";
        public List<decimal>? Spark { get; set; }
    }

    public class UnavailableMetricDto
    {
        public string Key { get; set; } = string.Empty;
        public string Label { get; set; } = string.Empty;
        public string Reason { get; set; } = string.Empty;
    }

    public class AnalyticsOverviewDto
    {
        public AnalyticsRangeDto Range { get; set; } = new();
        public List<KpiDto> Kpis { get; set; } = new();
        public List<UnavailableMetricDto> Unavailable { get; set; } = new();
        // True when the caller lacks ViewAnalytics, so user/learning KPIs were left out.
        public bool Restricted { get; set; }
        public List<BucketPointDto> UploadsLast7Days { get; set; } = new();
        public List<BucketPointDto> UploadsLast6Months { get; set; } = new();
    }

    public class BucketPointDto
    {
        // ISO date (yyyy-MM-dd) of the bucket start -- stable key for the chart axis.
        public string Start { get; set; } = string.Empty;
        public string Label { get; set; } = string.Empty;
        public int Count { get; set; }
    }

    // One row per time bucket carrying several named series. Series values the data cannot support for a
    // given endpoint are simply not present on the DTO that uses them.
    public class UserSeriesPointDto
    {
        public string Start { get; set; } = string.Empty;
        public string Label { get; set; } = string.Empty;
        public int NewUsers { get; set; }
        public int CumulativeUsers { get; set; }
        public int ActiveLearners { get; set; }   // distinct students who STARTED a test in the bucket
        public int TestAttempts { get; set; }     // attempts started in the bucket (events, not people)
    }

    public class AnalyticsUsersDto
    {
        public AnalyticsRangeDto Range { get; set; } = new();
        public List<UserSeriesPointDto> Series { get; set; } = new();
        public int UsersBeforeRange { get; set; }
        public List<string> Notes { get; set; } = new();
    }

    public class PublishingPointDto
    {
        public string Start { get; set; } = string.Empty;
        public string Label { get; set; } = string.Empty;
        public int Uploaded { get; set; }
        public int Published { get; set; }
        public int Rejected { get; set; }
    }

    public class DistributionSliceDto
    {
        public string Name { get; set; } = string.Empty;
        public int Count { get; set; }
        public double Percent { get; set; }
    }

    public class OrganizationPerformanceDto
    {
        public string Name { get; set; } = string.Empty;
        public int Exams { get; set; }
        public int PublishedPapers { get; set; }
        public int Learners { get; set; }         // distinct students who picked one of its exams in My Exams
    }

    public class QualityPointDto
    {
        public string Start { get; set; } = string.Empty;
        public string Label { get; set; } = string.Empty;
        public int Reported { get; set; }
        public int Resolved { get; set; }
    }

    public class StatusCountDto
    {
        public string Status { get; set; } = string.Empty;
        public int Count { get; set; }
    }

    public class AnalyticsContentDto
    {
        public AnalyticsRangeDto Range { get; set; } = new();
        public List<PublishingPointDto> Publishing { get; set; } = new();
        public List<DistributionSliceDto> SubjectDistribution { get; set; } = new();
        public List<DistributionSliceDto> ExamDistribution { get; set; } = new();
        public List<OrganizationPerformanceDto> Organizations { get; set; } = new();
        public List<string> Notes { get; set; } = new();
    }

    public class TestActivityPointDto
    {
        public string Start { get; set; } = string.Empty;
        public string Label { get; set; } = string.Empty;
        public int Started { get; set; }
        public int Completed { get; set; }
        public int Abandoned { get; set; }
        public int InProgress { get; set; }
    }

    public class ScatterPointDto
    {
        public double Minutes { get; set; }
        public double Accuracy { get; set; }
        public string Kind { get; set; } = string.Empty;
    }

    public class TestKindSummaryDto
    {
        public string Kind { get; set; } = string.Empty;
        public int Attempts { get; set; }
        public int Completed { get; set; }
        public double? AvgAccuracy { get; set; }
    }

    public class AnalyticsLearningDto
    {
        public AnalyticsRangeDto Range { get; set; } = new();
        public List<TestActivityPointDto> Activity { get; set; } = new();
        public List<ScatterPointDto> Scatter { get; set; } = new();
        public int ScatterTotalEligible { get; set; }
        public List<TestKindSummaryDto> ByKind { get; set; } = new();
        public List<string> Notes { get; set; } = new();
    }

    public class AnalyticsCommunityDto
    {
        public AnalyticsRangeDto Range { get; set; } = new();
        public List<QualityPointDto> QuestionReportsTrend { get; set; } = new();
        public List<StatusCountDto> QuestionReportsByStatus { get; set; } = new();   // reports filed in range
        public List<StatusCountDto> ChatReportsByStatus { get; set; } = new();
        public List<StatusCountDto> CommentReportsByStatus { get; set; } = new();
        public List<StatusCountDto> FeedbackByStatus { get; set; } = new();
        public int PendingSolutions { get; set; }
        public double? QuestionReportRejectionRate { get; set; }  // rejected / (resolved + rejected) in range
        public List<string> Notes { get; set; } = new();
    }

    public class AdminActivityPointDto
    {
        public string Start { get; set; } = string.Empty;
        public string Label { get; set; } = string.Empty;
        public Dictionary<string, int> ByModule { get; set; } = new();
        public int Total { get; set; }
    }

    public class CampaignSummaryDto
    {
        public string CampaignId { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public DateTime SentAt { get; set; }
        public int Recipients { get; set; }
        public int Read { get; set; }
    }

    public class AnalyticsOperationsDto
    {
        public AnalyticsRangeDto Range { get; set; } = new();
        public List<AdminActivityPointDto> AdminActivity { get; set; } = new();
        public List<string> Modules { get; set; } = new();
        public List<CampaignSummaryDto> RecentCampaigns { get; set; } = new();
        public List<UnavailableMetricDto> Unavailable { get; set; } = new();
        public List<string> Notes { get; set; } = new();
    }
}
