using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ScoramAPI.DTOs;
using ScoramAPI.Extensions;
using ScoramAPI.Services;

namespace ScoramAPI.Controllers
{
    /// <summary>SMART PROGRESS -- the rule-based daily study plan. Task completion is derived from real
    /// activity on the server; there is deliberately no "mark complete" endpoint.</summary>
    [ApiController]
    [Route("api/study-plan")]
    [Authorize(Roles = "Student")]
    public class StudyPlanController : ControllerBase
    {
        private readonly IStudyPlanService _plan;
        private readonly IMyExamScopeService _scope;

        public StudyPlanController(IStudyPlanService plan, IMyExamScopeService scope)
        {
            _plan = plan;
            _scope = scope;
        }

        [HttpGet("today")]
        [ProducesResponseType(typeof(StudyPlanDto), StatusCodes.Status200OK)]
        public async Task<ActionResult<StudyPlanDto>> Today(CancellationToken ct = default)
        {
            // Plan topics follow the student's My Exams when they have chosen some; with none chosen the
            // plan falls back to their whole history (it is their own data either way).
            var scope = await _scope.GetScopeAsync(User, ct);
            return Ok(await _plan.GetTodayAsync(User.GetUserId(), scope.ExamIds.Count > 0 ? scope.ExamIds : null, ct));
        }

        [HttpGet("history")]
        [ProducesResponseType(typeof(StudyPlanHistoryDto), StatusCodes.Status200OK)]
        public async Task<ActionResult<StudyPlanHistoryDto>> History([FromQuery] int days = 14, CancellationToken ct = default)
        {
            if (days < 1 || days > 60) return BadRequest(new { message = "days must be between 1 and 60." });
            return Ok(await _plan.GetHistoryAsync(User.GetUserId(), days, ct));
        }

        [HttpPatch("tasks/{taskId:guid}")]
        [ProducesResponseType(typeof(StudyTaskDto), StatusCodes.Status200OK)]
        public async Task<ActionResult<StudyTaskDto>> UpdateTask(Guid taskId, [FromBody] StudyTaskUpdateDto dto, CancellationToken ct = default)
        {
            var outcome = await _plan.SetDismissedAsync(User.GetUserId(), taskId, dto.Dismissed, ct);
            return outcome.Task != null
                ? Ok(outcome.Task)
                : StatusCode(outcome.StatusCode, new { message = outcome.Error ?? "Couldn't update that task." });
        }
    }
}
