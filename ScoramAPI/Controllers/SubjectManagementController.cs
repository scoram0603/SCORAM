using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ScoramAPI.DTOs;
using ScoramAPI.Enums;
using ScoramAPI.Extensions;
using ScoramAPI.Services;

namespace ScoramAPI.Controllers
{
    // Admin > Subjects. Manages the QuestionBankSubject master data that every PYQ / PYP / Practice /
    // Mock / Quiz dropdown and student-facing subject filter is built from -- including the risky
    // operations (rename, merge, reassign, delete) that rewrite content tagged with a subject.
    //
    // Authorization is enforced HERE, server-side, on every action (the admin UI hiding a button is
    // convenience only): the caller must be an Admin/SuperAdmin AND hold ManageSubjects (a Super Admin
    // implicitly holds every permission). Deliberately a separate permission from ManageQuestionBank --
    // see the enum's own comment.
    //
    // The existing subject endpoints under api/admin/question-bank/subjects (list/add/toggle) are left
    // exactly as they were, so the current "Subjects & Topics" screen and every dropdown built on them
    // keep working unchanged.
    [ApiController]
    [Route("api/admin/subjects")]
    [Authorize(Roles = "Admin,SuperAdmin")]
    public class SubjectManagementController : ControllerBase
    {
        private readonly ISubjectManagementService _subjects;
        private readonly IAdminPermissionService _permissions;

        public SubjectManagementController(ISubjectManagementService subjects, IAdminPermissionService permissions)
        {
            _subjects = subjects;
            _permissions = permissions;
        }

        // GET /api/admin/subjects?search=&status=all|active|inactive&sortBy=&sortDir=&page=&pageSize=
        [HttpGet]
        public Task<IActionResult> List([FromQuery] SubjectListQueryDto query, CancellationToken ct) =>
            Run(() => _subjects.ListAsync(query, ct));

        // GET /api/admin/subjects/{id} -- subject + full usage/dependency breakdown + its topics.
        [HttpGet("{id:guid}")]
        public Task<IActionResult> Get(Guid id, CancellationToken ct) =>
            Run(() => _subjects.GetAsync(id, ct));

        [HttpPost]
        public Task<IActionResult> Create(SubjectCreateRequestDto dto, CancellationToken ct) =>
            Run(() => _subjects.CreateAsync(User.GetAdminId(), dto, ct));

        // PUT /api/admin/subjects/{id} -- rename. Keeps the same record (same Id), so everything linked
        // by SubjectId is untouched; legacy PYP text is re-labelled in the same transaction.
        [HttpPut("{id:guid}")]
        public Task<IActionResult> Update(Guid id, SubjectUpdateRequestDto dto, CancellationToken ct) =>
            Run(() => _subjects.UpdateAsync(User.GetAdminId(), id, dto, ct));

        // PATCH /api/admin/subjects/{id}/active  { "isActive": true|false }
        // Deactivating is also how a subject is "archived" -- the project's existing soft-retire pattern
        // (QuestionBankSubject.IsActive); re-activating is "restore".
        [HttpPatch("{id:guid}/active")]
        public Task<IActionResult> SetActive(Guid id, SubjectSetActiveRequestDto dto, CancellationToken ct) =>
            Run(() => _subjects.SetActiveAsync(User.GetAdminId(), id, dto, ct));

        // POST /api/admin/subjects/merge/preview  -- impact numbers, nothing is changed.
        [HttpPost("merge/preview")]
        public Task<IActionResult> PreviewMerge(SubjectMergePreviewRequestDto dto, CancellationToken ct) =>
            Run(() => _subjects.PreviewMergeAsync(dto, ct));

        // POST /api/admin/subjects/merge -- runs in ONE database transaction; any failure rolls it all back.
        [HttpPost("merge")]
        public Task<IActionResult> Merge(SubjectMergeRequestDto dto, CancellationToken ct) =>
            Run(() => _subjects.MergeAsync(User.GetAdminId(), dto, ct));

        [HttpPost("reassign/preview")]
        public Task<IActionResult> PreviewReassign(SubjectReassignPreviewRequestDto dto, CancellationToken ct) =>
            Run(() => _subjects.PreviewReassignAsync(dto, ct));

        [HttpPost("reassign")]
        public Task<IActionResult> Reassign(SubjectReassignRequestDto dto, CancellationToken ct) =>
            Run(() => _subjects.ReassignAsync(User.GetAdminId(), dto, ct));

        // GET /api/admin/subjects/{id}/delete-preview -- can it be deleted, and if not, why not.
        [HttpGet("{id:guid}/delete-preview")]
        public Task<IActionResult> PreviewDelete(Guid id, CancellationToken ct) =>
            Run(() => _subjects.PreviewDeleteAsync(id, ct));

        // DELETE /api/admin/subjects/{id}?confirm=true&confirmName=<the subject's name>
        // Only ever succeeds for a subject with ZERO linked content; otherwise 409 SUBJECT_IN_USE with the counts.
        [HttpDelete("{id:guid}")]
        public Task<IActionResult> Delete(Guid id, [FromQuery] bool confirm, [FromQuery] string? confirmName, CancellationToken ct) =>
            Run(() => _subjects.DeleteAsync(User.GetAdminId(), id, confirm, confirmName, ct));

        // Permission gate + consistent error shape ({ message, code, data }) for every action above.
        private async Task<IActionResult> Run<T>(Func<Task<T>> action)
        {
            if (!await _permissions.HasPermissionAsync(User, AdminPermission.ManageSubjects))
                return Forbid();

            try
            {
                return Ok(await action());
            }
            catch (SubjectOpException ex)
            {
                return StatusCode(ex.Status, new { message = ex.Message, code = ex.Code, data = ex.Payload });
            }
        }
    }
}
