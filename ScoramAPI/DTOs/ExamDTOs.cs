using Microsoft.AspNetCore.Http;
using System.ComponentModel.DataAnnotations;

namespace ScoramAPI.DTOs
{
    // Bound with [FromForm] since it carries a file -- this is the "+ New Exam" step
    // (Enter Exam Name, Choose Exam Logo) of the admin PYQ upload wizard.
    public class ExamCreateDto
    {
        [Required, MaxLength(100)]
        public string Name { get; set; } = string.Empty;

        // Optional -- an exam can be created and used right away without a logo.
        public IFormFile? Logo { get; set; }

        // ORGANIZATION HIERARCHY -- optional, see Exam.OrganizationId's own comment: an exam can be
        // created with no Organization assigned and have one added later via Update.
        public Guid? OrganizationId { get; set; }
    }

    // ADMIN EXAM MANAGEMENT -- PATCH /api/admin/exams/{id}. Name/Logo are optional (partial update);
    // omit either to leave it unchanged. Logo, if provided, replaces the existing one.
    public class ExamUpdateDto
    {
        [MaxLength(100)]
        public string? Name { get; set; }
        public IFormFile? Logo { get; set; }

        // ORGANIZATION HIERARCHY -- omit the field entirely to leave the current assignment
        // unchanged; an admin who wants to explicitly clear it sends OrganizationId with
        // ClearOrganization=true (a plain null OrganizationId is indistinguishable from "not
        // provided" over [FromForm], so this needs its own flag -- see ExamsController.Update).
        public Guid? OrganizationId { get; set; }
        public bool ClearOrganization { get; set; }
    }

    // ADMIN EXAM MANAGEMENT -- PATCH /api/admin/exams/{id}/block
    public class ExamBlockDto
    {
        public bool IsBlocked { get; set; }
    }

    public class ExamResponseDto
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string? LogoUrl { get; set; }
        public bool IsBlocked { get; set; }
        public int QuestionCount { get; set; }
        public DateTime CreatedAt { get; set; }

        // ORGANIZATION HIERARCHY -- OrganizationName is carried alongside the id purely so exam
        // chips/lists can display it without a second lookup, same "denormalized display name"
        // pattern UserExamPreferenceDto already uses for its own ExamName.
        public Guid? OrganizationId { get; set; }
        public string? OrganizationName { get; set; }
    }

    // ---------- Force-delete-an-exam (Manage Exam "Delete" flow) ----------
    // The plain DELETE /api/admin/exams/{id} above only ever removes a genuinely empty exam. These
    // power the separate, much more dangerous "delete this exam and everything under it" flow --
    // see ExamsController.GetDeleteOptions/DeleteCascade for the full reasoning.

    // One row per Paper under the exam, for the "which papers?" selection step -- carries every
    // identity field (Year/Tier/Shift/Date/PaperCode/Language) a real exam might have set, so the
    // admin can tell two papers apart at a glance instead of just seeing a bare list of GUIDs.
    public class ExamDeletePaperOptionDto
    {
        public Guid PaperId { get; set; }
        public int Year { get; set; }
        public string? Tier { get; set; }
        public DateOnly? ExamDate { get; set; }
        public string? Shift { get; set; }
        public string? PaperLabel { get; set; }
        public string? PaperCode { get; set; }
        public string Language { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public int QuestionCount { get; set; }
    }

    // One row per distinct Year this exam has Question Bank (PYQ) content under -- Question Bank
    // only carries Exam+Year (see QuestionBankExamMapping), not Tier/Shift/Date, so Year is the only
    // selectable granularity on this side.
    public class ExamDeletePyqYearOptionDto
    {
        public int Year { get; set; }
        public int QuestionCount { get; set; }
    }

    // Aggregate counts of real student activity that a force-delete would remove along with the
    // content itself -- shown on the strict warning screen before the admin can even reach the
    // selection/confirm step. Spans both the legacy Question (PYP/Paper) side and the Question Bank
    // (PYQ) side of whatever's being deleted.
    public class ExamDeleteUsageCountsDto
    {
        public int StudentAnswers { get; set; }
        public int Bookmarks { get; set; }
        public int MockTestUsages { get; set; }
        public int QuizUsages { get; set; }
        public int PracticeTestUsages { get; set; }
        public int Comments { get; set; }
        public int Votes { get; set; }
        public int Reports { get; set; }
        public int Solutions { get; set; }
    }

    public class ExamDeleteOptionsDto
    {
        public Guid ExamId { get; set; }
        public string ExamName { get; set; } = string.Empty;
        public List<ExamDeletePaperOptionDto> Papers { get; set; } = new();
        public List<ExamDeletePyqYearOptionDto> PyqYears { get; set; } = new();

        // Pre-Paper-era standalone questions (Question.ExamId set directly, no Paper) -- these have
        // no per-item identity to select against, so they're only ever removed by DeleteAll, never
        // selectively. Surfaced here just so the warning screen's totals are honest about them.
        public int LegacyQuestionCount { get; set; }

        public ExamDeleteUsageCountsDto Usage { get; set; } = new();
    }

    // POST /api/admin/exams/{id}/delete-cascade body. Exactly one of "delete everything" or a
    // specific selection -- see ExamsController.DeleteCascade for how DeleteAll vs PaperIds/PyqYears
    // is resolved. ConfirmExamName is a server-side re-check of the same "type the exam's name to
    // confirm" the frontend already asks for -- never trust that gate to the client alone.
    public class ExamDeleteRequestDto
    {
        [Required]
        public string ConfirmExamName { get; set; } = string.Empty;
        public bool DeleteAll { get; set; }
        public List<Guid>? PaperIds { get; set; }
        public List<int>? PyqYears { get; set; }
    }

    public class ExamDeleteResultDto
    {
        public bool ExamDeleted { get; set; }
        public int PapersDeleted { get; set; }
        public int PypQuestionsDeleted { get; set; }
        public int PyqQuestionsDeleted { get; set; }
        public int PyqMappingsRemoved { get; set; }
    }
}
