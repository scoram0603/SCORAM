using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ScoramAPI.DTOs;
using ScoramAPI.Extensions;
using ScoramAPI.Services;

namespace ScoramAPI.Controllers
{
    // Business ID administration. SuperAdmin ONLY -- both endpoints are gated by
    // [Authorize(Roles = "SuperAdmin")], and BusinessIdService.ChangeAsync re-checks the live account.
    //
    // There is intentionally no endpoint for normal admins to change a Business ID: the create/update
    // DTOs for exams, subjects, tests, mock tests and admins do not carry the field at all, and
    // ScoramDbContext refuses any other write to an existing BusinessId.
    [ApiController]
    [Route("api/admin/business-ids")]
    [Authorize(Roles = "SuperAdmin")]
    public class BusinessIdsController : ControllerBase
    {
        private readonly IBusinessIdService _businessIds;

        public BusinessIdsController(IBusinessIdService businessIds)
        {
            _businessIds = businessIds;
        }

        // POST /api/admin/business-ids/change
        // { "entityType": "exam", "entityId": "<guid>", "newBusinessId": "EXMSSC010", "confirm": true }
        // Changes ONLY the Business ID. The GUID and every foreign key stay exactly as they were.
        [HttpPost("change")]
        public Task<IActionResult> Change(BusinessIdChangeRequestDto dto, CancellationToken ct) =>
            Run(() => _businessIds.ChangeAsync(User.GetAdminId(), dto, ct));

        // POST /api/admin/business-ids/backfill?dryRun=true
        // Numbers every existing record that has no Business ID yet. dryRun=true (the default here, so a
        // stray call can't assign permanent IDs) returns the plan without saving anything; pass
        // dryRun=false to commit it. Normally this has already happened automatically at startup --
        // this endpoint exists for deployments that set BusinessIds:AutoBackfillOnStartup=false to
        // review the plan first.
        [HttpPost("backfill")]
        public Task<IActionResult> Backfill([FromQuery] bool dryRun = true, CancellationToken ct = default) =>
            Run(() => _businessIds.BackfillAsync(dryRun, ct));

        private async Task<IActionResult> Run<T>(Func<Task<T>> action)
        {
            try
            {
                return Ok(await action());
            }
            catch (BusinessIdException ex)
            {
                return StatusCode(ex.Status, new { message = ex.Message, code = ex.Code });
            }
        }
    }
}
