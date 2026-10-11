using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using ScoramAPI.DTOs;
using ScoramAPI.Enums;
using ScoramAPI.Extensions;
using ScoramAPI.Services;
using ScoramAPI.Services.Analytics;

namespace ScoramAPI.Controllers
{
    /// <summary>
    /// SMART PROGRESS -- the student's analytics (overview, subjects, topics, trends, one assessment).
    /// Identity always comes from the JWT; no endpoint accepts a user id, so there is nothing to tamper
    /// with. Definitions live in SMART_PROGRESS.md and Services/Analytics.
    /// </summary>
    [ApiController]
    [Route("api/progress")]
    [Authorize(Roles = "Student")]
    public class ProgressController : ControllerBase
    {
        private readonly IProgressAnalyticsService _analytics;
        private readonly IStudyPlanService _plan;
        private readonly IMyExamScopeService _scope;
        private readonly AnalyticsOptions _o;

        public ProgressController(IProgressAnalyticsService analytics, IStudyPlanService plan, IMyExamScopeService scope, IOptions<AnalyticsOptions> options)
        {
            _analytics = analytics;
            _plan = plan;
            _scope = scope;
            _o = options.Value;
        }

        /// <summary>Validates the shared query filters. Returns an error message, or null when valid.</summary>
        internal static async Task<(ProgressFilter? Filter, string? Error)> ParseFilterAsync(
            ControllerBase c, IMyExamScopeService scope, AnalyticsOptions o, Guid? examId, string? kind, int? days, bool myExamsOnly, CancellationToken ct)
        {
            var f = new ProgressFilter { ExamId = examId, MyExamsOnly = myExamsOnly };

            if (!string.IsNullOrWhiteSpace(kind))
            {
                if (!Enum.TryParse<TestKind>(kind.Trim(), ignoreCase: true, out var k) || !Enum.IsDefined(typeof(TestKind), k))
                    return (null, "kind must be one of Practice, Mock, PreviousYearPaper, Quiz.");
                f.Kind = k;
            }
            if (days.HasValue)
            {
                if (days.Value < 1 || days.Value > o.MaxHistoryDays)
                    return (null, $"days must be between 1 and {o.MaxHistoryDays}.");
                f.Days = days;
            }
            if (examId.HasValue && examId.Value == Guid.Empty) return (null, "examId is not valid.");

            List<Guid>? exams = null;
            if (myExamsOnly)
            {
                var s = await scope.GetScopeAsync(c.User, ct);
                exams = s.ExamIds.ToList(); // may be empty => nothing matches, never "everything"
            }
            if (examId.HasValue)
                exams = exams == null ? new List<Guid> { examId.Value } : exams.Where(e => e == examId.Value).ToList();
            f.ExamIds = exams;
            return (f, null);
        }

        [HttpGet("overview")]
        [ProducesResponseType(typeof(ProgressOverviewDto), StatusCodes.Status200OK)]
        public async Task<ActionResult<ProgressOverviewDto>> Overview(
            [FromQuery] Guid? examId, [FromQuery] string? kind, [FromQuery] int? days, [FromQuery] bool myExamsOnly = false, CancellationToken ct = default)
        {
            var (f, error) = await ParseFilterAsync(this, _scope, _o, examId, kind, days, myExamsOnly, ct);
            if (f == null) return BadRequest(new { message = error });

            var userId = User.GetUserId();
            var dto = await _analytics.GetOverviewAsync(userId, f, ct);
            dto.StudyPlan = await _plan.GetSummaryAsync(userId, AnalyticsCalculator.IstDay(DateTime.UtcNow), ct);
            return Ok(dto);
        }

        [HttpGet("subjects")]
        [ProducesResponseType(typeof(SubjectsResponseDto), StatusCodes.Status200OK)]
        public async Task<ActionResult<SubjectsResponseDto>> Subjects(
            [FromQuery] Guid? examId, [FromQuery] string? kind, [FromQuery] int? days, [FromQuery] bool myExamsOnly = false,
            [FromQuery] bool includeTopics = true, CancellationToken ct = default)
        {
            var (f, error) = await ParseFilterAsync(this, _scope, _o, examId, kind, days, myExamsOnly, ct);
            if (f == null) return BadRequest(new { message = error });
            return Ok(await _analytics.GetSubjectsAsync(User.GetUserId(), f, includeTopics, ct));
        }

        [HttpGet("topics/detail")]
        [ProducesResponseType(typeof(TopicDetailDto), StatusCodes.Status200OK)]
        public async Task<ActionResult<TopicDetailDto>> TopicDetail(
            [FromQuery] string? subject, [FromQuery] string? topic,
            [FromQuery] Guid? examId, [FromQuery] string? kind, [FromQuery] int? days, [FromQuery] bool myExamsOnly = false, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(subject) || subject.Length > 150) return BadRequest(new { message = "subject is required (max 150 characters)." });
            if (string.IsNullOrWhiteSpace(topic) || topic.Length > 200) return BadRequest(new { message = "topic is required (max 200 characters)." });

            var (f, error) = await ParseFilterAsync(this, _scope, _o, examId, kind, days, myExamsOnly, ct);
            if (f == null) return BadRequest(new { message = error });

            var detail = await _analytics.GetTopicDetailAsync(User.GetUserId(), f, subject!.Trim(), topic!.Trim(), ct);
            if (detail == null) return NotFound(new { message = "No answers recorded for that topic yet." });
            return Ok(detail);
        }

        [HttpGet("trends")]
        [ProducesResponseType(typeof(TrendsDto), StatusCodes.Status200OK)]
        public async Task<ActionResult<TrendsDto>> Trends(
            [FromQuery] Guid? examId, [FromQuery] string? kind, [FromQuery] bool myExamsOnly = false, CancellationToken ct = default)
        {
            var (f, error) = await ParseFilterAsync(this, _scope, _o, examId, kind, null, myExamsOnly, ct);
            if (f == null) return BadRequest(new { message = error });
            return Ok(await _analytics.GetTrendsAsync(User.GetUserId(), f, ct));
        }

        /// <summary>Analytics for ONE submitted assessment (any kind). 404 when it doesn't exist, isn't
        /// yours or isn't finished -- indistinguishable on purpose, so ids can't be probed.</summary>
        [HttpGet("assessments/{attemptId:guid}/analytics")]
        [ProducesResponseType(typeof(AttemptAnalyticsDto), StatusCodes.Status200OK)]
        public async Task<ActionResult<AttemptAnalyticsDto>> AssessmentAnalytics(Guid attemptId, CancellationToken ct = default)
        {
            var dto = await _analytics.GetAttemptAnalyticsAsync(User.GetUserId(), attemptId, ct);
            if (dto == null) return NotFound(new { message = "Assessment not found." });
            return Ok(dto);
        }
    }
}
