using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ScoramAPI.DTOs;
using ScoramAPI.Enums;
using ScoramAPI.Extensions;
using ScoramAPI.Services;

namespace ScoramAPI.Controllers
{
    // Admin > Exam-wise Subject & Topic Management. Manages the per-exam subject/topic taxonomy (ExamSubject /
    // ExamTopic) and the per-exam classification of Question Bank questions -- never the global subjects, and
    // never question content. See Models/ExamTaxonomyModels.cs for the model and Services/ExamTaxonomyService.cs
    // for the rules.
    //
    // Authorization is enforced HERE, server-side, on every action (the UI hiding a button is only a
    // convenience): the caller must be an Admin/SuperAdmin holding ManageSubjects (Super Admin implicitly holds
    // everything). Mapping/unmapping questions additionally accepts ManageQuestionBank, since classifying
    // questions is also part of curating the question bank.
    //
    // Errors use the same { message, code, data } shape as Subject Management.
    [ApiController]
    [Route("api/admin/exam-taxonomy")]
    [Authorize(Roles = "Admin,SuperAdmin")]
    public class ExamTaxonomyController : ControllerBase
    {
        private readonly IExamTaxonomyService _taxonomy;
        private readonly IAdminPermissionService _permissions;

        public ExamTaxonomyController(IExamTaxonomyService taxonomy, IAdminPermissionService permissions)
        {
            _taxonomy = taxonomy;
            _permissions = permissions;
        }

        // GET /api/admin/exam-taxonomy/exams?search=
        [HttpGet("exams")]
        public Task<IActionResult> Exams([FromQuery] string? search, CancellationToken ct) =>
            Run(() => _taxonomy.ListExamsAsync(search, ct));

        // GET /api/admin/exam-taxonomy/{examId}?search=&status=all|active|archived -- the exam's subjects + counts
        [HttpGet("{examId:guid}")]
        public Task<IActionResult> Overview(Guid examId, [FromQuery] string? search, [FromQuery] string? status, CancellationToken ct) =>
            Run(() => _taxonomy.GetOverviewAsync(examId, search, status, ct));

        // ---------------- initialise from legacy classification ----------------
        [HttpGet("{examId:guid}/initialize/preview")]
        public Task<IActionResult> InitPreview(Guid examId, CancellationToken ct) =>
            Run(() => _taxonomy.PreviewInitializeAsync(examId, ct));

        [HttpPost("{examId:guid}/initialize")]
        public Task<IActionResult> Initialize(Guid examId, ExamTaxonomyInitRequestDto dto, CancellationToken ct) =>
            Run(() => _taxonomy.InitializeAsync(User.GetAdminId(), examId, dto, ct));

        // ---------------- subjects ----------------
        [HttpPost("{examId:guid}/subjects")]
        public Task<IActionResult> CreateSubject(Guid examId, ExamSubjectCreateDto dto, CancellationToken ct) =>
            Run(() => _taxonomy.CreateSubjectAsync(User.GetAdminId(), examId, dto, ct));

        // PUT -- rename for THIS exam only (same record/Id; every other exam is untouched).
        [HttpPut("{examId:guid}/subjects/{id:guid}")]
        public Task<IActionResult> RenameSubject(Guid examId, Guid id, ExamTaxonomyRenameDto dto, CancellationToken ct) =>
            Run(() => _taxonomy.RenameSubjectAsync(User.GetAdminId(), examId, id, dto, ct));

        // PATCH -- archive (isActive=false) / restore (isActive=true)
        [HttpPatch("{examId:guid}/subjects/{id:guid}/active")]
        public Task<IActionResult> SetSubjectActive(Guid examId, Guid id, ExamTaxonomySetActiveDto dto, CancellationToken ct) =>
            Run(() => _taxonomy.SetSubjectActiveAsync(User.GetAdminId(), examId, id, dto, ct));

        [HttpGet("{examId:guid}/subjects/{id:guid}/usage")]
        public Task<IActionResult> SubjectUsage(Guid examId, Guid id, CancellationToken ct) =>
            Run(() => _taxonomy.GetSubjectUsageAsync(examId, id, ct));

        [HttpGet("{examId:guid}/subjects/{id:guid}/delete-preview")]
        public Task<IActionResult> PreviewDeleteSubject(Guid examId, Guid id, CancellationToken ct) =>
            Run(() => _taxonomy.PreviewDeleteSubjectAsync(examId, id, ct));

        // DELETE ?mode=delete|unassign&confirm=true&confirmName=<name>&expectedQuestions=<n>
        // mode=delete only succeeds when nothing depends on the subject; mode=unassign first clears the
        // classification of its questions (never deleting them), then removes the subject and its topics.
        [HttpDelete("{examId:guid}/subjects/{id:guid}")]
        public Task<IActionResult> DeleteSubject(Guid examId, Guid id, [FromQuery] string? mode, [FromQuery] bool confirm,
            [FromQuery] string? confirmName, [FromQuery] int? expectedQuestions, CancellationToken ct) =>
            Run(() => _taxonomy.DeleteSubjectAsync(User.GetAdminId(), examId, id, mode, confirm, confirmName, expectedQuestions, ct));

        // ---------------- topics ----------------
        [HttpGet("{examId:guid}/subjects/{subjectId:guid}/topics")]
        public Task<IActionResult> ListTopics(Guid examId, Guid subjectId, [FromQuery] string? search, [FromQuery] string? status, CancellationToken ct) =>
            Run(() => _taxonomy.ListTopicsAsync(examId, subjectId, search, status, ct));

        [HttpPost("{examId:guid}/subjects/{subjectId:guid}/topics")]
        public Task<IActionResult> CreateTopic(Guid examId, Guid subjectId, ExamTopicCreateDto dto, CancellationToken ct) =>
            Run(() => _taxonomy.CreateTopicAsync(User.GetAdminId(), examId, subjectId, dto, ct));

        [HttpPut("{examId:guid}/topics/{id:guid}")]
        public Task<IActionResult> RenameTopic(Guid examId, Guid id, ExamTaxonomyRenameDto dto, CancellationToken ct) =>
            Run(() => _taxonomy.RenameTopicAsync(User.GetAdminId(), examId, id, dto, ct));

        [HttpPatch("{examId:guid}/topics/{id:guid}/active")]
        public Task<IActionResult> SetTopicActive(Guid examId, Guid id, ExamTaxonomySetActiveDto dto, CancellationToken ct) =>
            Run(() => _taxonomy.SetTopicActiveAsync(User.GetAdminId(), examId, id, dto, ct));

        [HttpGet("{examId:guid}/topics/{id:guid}/usage")]
        public Task<IActionResult> TopicUsage(Guid examId, Guid id, CancellationToken ct) =>
            Run(() => _taxonomy.GetTopicUsageAsync(examId, id, ct));

        [HttpGet("{examId:guid}/topics/{id:guid}/delete-preview")]
        public Task<IActionResult> PreviewDeleteTopic(Guid examId, Guid id, CancellationToken ct) =>
            Run(() => _taxonomy.PreviewDeleteTopicAsync(examId, id, ct));

        [HttpDelete("{examId:guid}/topics/{id:guid}")]
        public Task<IActionResult> DeleteTopic(Guid examId, Guid id, [FromQuery] string? mode, [FromQuery] bool confirm,
            [FromQuery] string? confirmName, [FromQuery] int? expectedQuestions, CancellationToken ct) =>
            Run(() => _taxonomy.DeleteTopicAsync(User.GetAdminId(), examId, id, mode, confirm, confirmName, expectedQuestions, ct));

        // ---------------- question mapping ----------------
        // GET ?examSubjectId=&examTopicId=&unassigned=true&search=&page=&pageSize=
        [HttpGet("{examId:guid}/questions")]
        public Task<IActionResult> Questions(Guid examId, [FromQuery] ExamTaxonomyQuestionQueryDto query, CancellationToken ct) =>
            Run(() => _taxonomy.ListQuestionsAsync(examId, query, ct));

        [HttpPost("{examId:guid}/questions/map")]
        public Task<IActionResult> Map(Guid examId, ExamTaxonomyMapRequestDto dto, CancellationToken ct) =>
            Run(() => _taxonomy.MapQuestionsAsync(User.GetAdminId(), examId, dto, ct), allowQuestionBankPermission: true);

        [HttpPost("{examId:guid}/questions/unmap")]
        public Task<IActionResult> Unmap(Guid examId, ExamTaxonomyUnmapRequestDto dto, CancellationToken ct) =>
            Run(() => _taxonomy.UnmapQuestionsAsync(User.GetAdminId(), examId, dto, ct), allowQuestionBankPermission: true);

        // GET -- how ONE question is classified in every exam it belongs to (same question, different exams).
        [HttpGet("question/{questionId:guid}/classifications")]
        public Task<IActionResult> QuestionClassifications(Guid questionId, CancellationToken ct) =>
            Run(() => _taxonomy.GetQuestionClassificationsAsync(questionId, ct), allowQuestionBankPermission: true);

        // Permission gate + consistent error shape ({ message, code, data }) for every action above.
        private async Task<IActionResult> Run<T>(Func<Task<T>> action, bool allowQuestionBankPermission = false)
        {
            var allowed = await _permissions.HasPermissionAsync(User, AdminPermission.ManageSubjects)
                || (allowQuestionBankPermission && await _permissions.HasPermissionAsync(User, AdminPermission.ManageQuestionBank));
            if (!allowed) return Forbid();

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
