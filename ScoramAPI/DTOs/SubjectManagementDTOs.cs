namespace ScoramAPI.DTOs
{
    // ==================================================================================
    // Subject Management (admin > Subjects). A "Subject" here is the existing
    // QuestionBankSubject master-data row -- the same one every PYQ (Question Bank) question,
    // Topic, Practice Test template and student-facing subject filter already points at by
    // SubjectId. Legacy PYP paper questions (Question.Subject) still store the subject as plain
    // text, so they are linked to a Subject BY NAME (case-insensitive) -- see
    // SubjectManagementService for how that's counted and kept in sync on rename/merge.
    // ==================================================================================

    // What currently uses one subject. Split in two on purpose:
    //   * "Affected" rows are the ones a rename/merge/reassign actually REWRITES.
    //   * "Follows automatically" content is only ever *derived* from those questions (a Mock
    //     Test / Quiz / Paper has no SubjectId of its own -- it just contains questions), so it
    //     needs no update and is shown for information only.
    public class SubjectUsageDto
    {
        // ----- Rewritten by merge / reassign -----
        // Question Bank ("PYQ") questions carrying this SubjectId -- every row, inactive included.
        public int QuestionBankQuestions { get; set; }
        public int InactiveQuestionBankQuestions { get; set; }
        // Legacy PYP paper questions whose text Subject matches this subject's name.
        public int PypQuestions { get; set; }
        public int Topics { get; set; }
        public int PracticeTemplates { get; set; }

        // Sum of the four above -- what "Total affected records" means everywhere in the UI.
        public int TotalAffected { get; set; }

        // ----- Follows automatically (never edited by a merge/reassign) -----
        // De-duplicated question total (a PYP question is auto-mirrored into the Question Bank --
        // same rule PublicStatsController/ExamsController use, so the same question isn't counted twice).
        public int TotalQuestions { get; set; }
        // Number of question slots in Mock Tests / Quizzes that hold a question of this subject.
        public int MockTestQuestions { get; set; }
        public int QuizQuestions { get; set; }
        // Distinct Mock Tests / Quizzes / Papers containing at least one question of this subject.
        public int MockTests { get; set; }
        public int Quizzes { get; set; }
        public int Papers { get; set; }
        // Past Practice attempts whose saved filter snapshot points at this SubjectId. Only filled
        // in for detail/preview/delete (a scan of the attempts table) -- 0 on the list page.
        public int PracticeAttempts { get; set; }
    }

    public class SubjectListItemDto
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public bool IsActive { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? UpdatedAt { get; set; }
        // Opaque optimistic-concurrency token -- echo it back on rename/activate so an edit made on
        // a stale copy of the row is rejected instead of silently overwriting someone else's change.
        public string Version { get; set; } = string.Empty;
        public SubjectUsageDto Usage { get; set; } = new();
    }

    public class SubjectTopicUsageDto
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public bool IsActive { get; set; }
        public int QuestionCount { get; set; }
    }

    public class SubjectDetailDto : SubjectListItemDto
    {
        public List<SubjectTopicUsageDto> TopicList { get; set; } = new();
    }

    public class SubjectListQueryDto
    {
        public string? Search { get; set; }
        // all | active | inactive
        public string? Status { get; set; }
        // name | status | questions | pyp | pyq | mock | quiz | created | updated
        public string? SortBy { get; set; }
        // asc | desc
        public string? SortDir { get; set; }
        public int Page { get; set; } = 1;
        public int PageSize { get; set; } = 20;
    }

    public class SubjectListResponseDto
    {
        public List<SubjectListItemDto> Items { get; set; } = new();
        public int Total { get; set; }
        public int Page { get; set; }
        public int PageSize { get; set; }
        // Whole-table status counts (ignoring search/status filter) for the filter chips.
        public int ActiveCount { get; set; }
        public int InactiveCount { get; set; }
    }

    // ---------- Requests ----------

    public class SubjectCreateRequestDto
    {
        public string Name { get; set; } = string.Empty;
        public bool IsActive { get; set; } = true;
    }

    public class SubjectUpdateRequestDto
    {
        public string Name { get; set; } = string.Empty;
        // From SubjectListItemDto.Version; optional but the UI always sends it.
        public string? Version { get; set; }
        // Must be true when the subject is in use -- the "This subject is currently used by N
        // records..." confirmation. Enforced here too, not just in the UI.
        public bool Confirm { get; set; }
    }

    public class SubjectSetActiveRequestDto
    {
        public bool IsActive { get; set; }
        public string? Version { get; set; }
    }

    public class SubjectMergeRequestDto
    {
        public List<Guid> SourceIds { get; set; } = new();
        public Guid TargetId { get; set; }
        public bool Confirm { get; set; }
        // Admin must type the TARGET subject's name -- so a merge can never fire from one stray click.
        public string? ConfirmName { get; set; }
        // TotalAffected the admin saw in the preview; if the live number differs by execution
        // time (someone imported/edited content meanwhile) the merge is refused and re-previewed.
        public int? ExpectedTotalAffected { get; set; }
    }

    public class SubjectMergePreviewRequestDto
    {
        public List<Guid> SourceIds { get; set; } = new();
        public Guid TargetId { get; set; }
    }

    public class SubjectReassignRequestDto
    {
        public Guid SourceId { get; set; }
        public Guid TargetId { get; set; }
        public bool Confirm { get; set; }
        public int? ExpectedTotalAffected { get; set; }
    }

    public class SubjectReassignPreviewRequestDto
    {
        public Guid SourceId { get; set; }
        public Guid TargetId { get; set; }
    }

    // ---------- Responses ----------

    public class SubjectImpactItemDto
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public bool IsActive { get; set; }
        public SubjectUsageDto Usage { get; set; } = new();
    }

    // Impact preview for merge / reassign. Every number comes from live queries -- never hard-coded.
    public class SubjectImpactPreviewDto
    {
        // "merge" | "reassign"
        public string Operation { get; set; } = string.Empty;
        public List<SubjectImpactItemDto> Sources { get; set; } = new();
        public SubjectImpactItemDto Target { get; set; } = new();
        // Sum across all sources.
        public SubjectUsageDto Combined { get; set; } = new();
        // Topics that will be moved under the target as-is.
        public int TopicsToMove { get; set; }
        // Topics whose name already exists under the target -- their questions fold into that
        // existing target topic instead of creating a duplicate.
        public int TopicsToCombine { get; set; }
        public List<string> Warnings { get; set; } = new();
        // Reasons the operation can't run right now. Empty => CanProceed.
        public List<string> Blockers { get; set; } = new();
        public bool CanProceed => Blockers.Count == 0;
    }

    public class SubjectDeletePreviewDto
    {
        public SubjectListItemDto Subject { get; set; } = new();
        public bool CanDelete { get; set; }
        // Human-readable dependency lines, e.g. "1,250 Question Bank questions".
        public List<string> Blockers { get; set; } = new();
    }

    public class SubjectOperationResultDto
    {
        public string Message { get; set; } = string.Empty;
        // Rows actually rewritten.
        public int RecordsUpdated { get; set; }
        public int TopicsMoved { get; set; }
        public int TopicsCombined { get; set; }
        public SubjectListItemDto? Subject { get; set; }
    }
}
