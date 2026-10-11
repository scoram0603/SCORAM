using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using ScoramAPI.DTOs;
using ScoramAPI.Enums;
using ScoramAPI.Extensions;
using ScoramAPI.Services;
using ScoramAPI.Services.Analytics;

namespace ScoramAPI.Controllers
{
    /// <summary>SMART PROGRESS -- Mistake Book + revision loop. Revision answers never touch an original
    /// assessment's score/rank/XP (see RevisionService).</summary>
    [ApiController]
    [Route("api/revision")]
    [Authorize(Roles = "Student")]
    public class RevisionController : ControllerBase
    {
        private static readonly string[] Statuses = { "open", "new", "learning", "corrected", "dismissed", "all" };
        private static readonly string[] Types = { "incorrect", "skipped", "repeated", "marked", "all" };
        private static readonly string[] Sorts = { "recent", "oldest", "mostwrong" };

        private readonly IRevisionService _revision;
        private readonly IMyExamScopeService _scope;
        private readonly AnalyticsOptions _o;

        public RevisionController(IRevisionService revision, IMyExamScopeService scope, IOptions<AnalyticsOptions> options)
        {
            _revision = revision;
            _scope = scope;
            _o = options.Value;
        }

        private async Task<(MistakeQuery? Query, string? Error)> BuildQueryAsync(
            Guid? examId, string? kind, string? subject, string? topic, string? search, string? status, string? type,
            bool dueOnly, string? sort, bool myExamsOnly, int page, int pageSize, CancellationToken ct)
        {
            var (f, error) = await ProgressController.ParseFilterAsync(this, _scope, _o, examId, kind, null, myExamsOnly, ct);
            if (f == null) return (null, error);

            if (subject != null && subject.Length > 150) return (null, "subject is too long.");
            if (topic != null && topic.Length > 200) return (null, "topic is too long.");
            if (search != null && search.Length > 100) return (null, "search is too long (max 100 characters).");
            if (status != null && !Statuses.Contains(status.Trim().ToLowerInvariant())) return (null, "status must be Open, New, Learning, Corrected, Dismissed or All.");
            if (type != null && !Types.Contains(type.Trim().ToLowerInvariant())) return (null, "type must be Incorrect, Skipped, Repeated, Marked or All.");
            if (sort != null && !Sorts.Contains(sort.Trim().ToLowerInvariant())) return (null, "sort must be recent, oldest or mostWrong.");
            if (page < 1) return (null, "page must be 1 or more.");
            if (pageSize < 1 || pageSize > _o.MaxPageSize) return (null, $"pageSize must be between 1 and {_o.MaxPageSize}.");

            return (new MistakeQuery
            {
                ExamIds = f.ExamIds,
                Kind = f.Kind,
                Subject = subject,
                Topic = topic,
                Search = search,
                Status = status?.Trim() ?? "Open",
                Type = type?.Trim() ?? "Incorrect",
                DueOnly = dueOnly,
                Sort = sort?.Trim() ?? "recent",
                Page = page,
                PageSize = pageSize
            }, null);
        }

        [HttpGet("mistakes")]
        [ProducesResponseType(typeof(MistakeBookDto), StatusCodes.Status200OK)]
        public async Task<ActionResult<MistakeBookDto>> Mistakes(
            [FromQuery] Guid? examId, [FromQuery] string? kind, [FromQuery] string? subject, [FromQuery] string? topic,
            [FromQuery] string? search, [FromQuery] string? status, [FromQuery] string? type, [FromQuery] bool dueOnly = false,
            [FromQuery] string? sort = null, [FromQuery] bool myExamsOnly = false, [FromQuery] int page = 1, [FromQuery] int pageSize = 20,
            CancellationToken ct = default)
        {
            var (q, error) = await BuildQueryAsync(examId, kind, subject, topic, search, status, type, dueOnly, sort, myExamsOnly, page, pageSize, ct);
            if (q == null) return BadRequest(new { message = error });
            return Ok(await _revision.GetMistakeBookAsync(User.GetUserId(), q, ct));
        }

        [HttpGet("mistakes/summary")]
        [ProducesResponseType(typeof(RevisionSummaryDto), StatusCodes.Status200OK)]
        public async Task<ActionResult<RevisionSummaryDto>> Summary(CancellationToken ct = default)
            => Ok(await _revision.GetSummaryAsync(User.GetUserId(), ct));

        [HttpGet("mistakes/detail")]
        [ProducesResponseType(typeof(MistakeDetailDto), StatusCodes.Status200OK)]
        public async Task<ActionResult<MistakeDetailDto>> Detail([FromQuery(Name = "ref")] string? questionRef, CancellationToken ct = default)
        {
            if (!QuestionKey.TryParse(questionRef, out var key)) return BadRequest(new { message = "ref is not a valid question reference." });
            var detail = await _revision.GetMistakeDetailAsync(User.GetUserId(), key, ct);
            if (detail == null) return NotFound(new { message = "That question isn't in your history." });
            return Ok(detail);
        }

        [HttpGet("session")]
        [ProducesResponseType(typeof(RevisionSessionDto), StatusCodes.Status200OK)]
        public async Task<ActionResult<RevisionSessionDto>> Session(
            [FromQuery] int count = 10, [FromQuery] Guid? examId = null, [FromQuery] string? kind = null,
            [FromQuery] string? subject = null, [FromQuery] string? topic = null, [FromQuery] bool dueOnly = false,
            [FromQuery] bool myExamsOnly = false, CancellationToken ct = default)
        {
            if (count < 1 || count > _o.MaxSessionQuestions) return BadRequest(new { message = $"count must be between 1 and {_o.MaxSessionQuestions}." });
            var (q, error) = await BuildQueryAsync(examId, kind, subject, topic, null, "Open", "Incorrect", dueOnly, "recent", myExamsOnly, 1, 20, ct);
            if (q == null) return BadRequest(new { message = error });
            return Ok(await _revision.GetSessionAsync(User.GetUserId(), q, count, ct));
        }

        [HttpPost("answers")]
        [EnableRateLimiting("smart-progress-write")]
        [ProducesResponseType(typeof(RevisionAnswerResultDto), StatusCodes.Status200OK)]
        public async Task<ActionResult<RevisionAnswerResultDto>> Answer([FromBody] RevisionAnswerRequestDto dto, CancellationToken ct = default)
        {
            if (!QuestionKey.TryParse(dto.QuestionRef, out var key)) return BadRequest(new { message = "questionRef is not a valid question reference." });
            if (!Enum.TryParse<OptionLetter>(dto.SelectedOption?.Trim(), ignoreCase: true, out var option) || !Enum.IsDefined(typeof(OptionLetter), option))
                return BadRequest(new { message = "selectedOption must be A, B, C or D." });

            var outcome = await _revision.AnswerAsync(User.GetUserId(), key, option, dto.TimeSpentSeconds, dto.ClientRequestId, ct);
            return outcome.Result != null
                ? Ok(outcome.Result)
                : StatusCode(outcome.StatusCode, new { message = outcome.Error ?? "Couldn't record that answer." });
        }

        [HttpPut("mistakes/dismiss")]
        public async Task<IActionResult> Dismiss([FromQuery(Name = "ref")] string? questionRef, [FromBody] MistakeDismissDto dto, CancellationToken ct = default)
        {
            if (!QuestionKey.TryParse(questionRef, out var key)) return BadRequest(new { message = "ref is not a valid question reference." });
            var ok = await _revision.SetDismissedAsync(User.GetUserId(), key, dto.Dismissed, ct);
            return ok ? NoContent() : NotFound(new { message = "That question isn't in your history." });
        }
    }
}
