namespace ScoramAPI.Enums
{
    public enum AdminRole
    {
        SuperAdmin,
        Admin
    }

    public enum DifficultyLevel
    {
        Easy,
        Medium,
        Hard
    }

    public enum OptionLetter
    {
        A,
        B,
        C,
        D
    }

    public enum SolutionType
    {
        OfficialAdmin,
        TeacherVerified,
        Community,
        Shortcut,
        Alternative
    }

    public enum ReportType
    {
        WrongAnswer,
        WrongQuestionStatement,
        IncorrectExplanation,
        TypingMistake,
        Duplicate,
        // Added for the Question Bank "Report Question" feature (SCORAM_QUESTION_BANK) -- kept in
        // the same shared enum as the original four so legacy PYQ questions and Question Bank
        // questions report through one consistent set of reasons instead of two parallel lists.
        WrongOption,
        IncorrectExamYear,
        Other
    }

    public enum ReportStatus
    {
        Pending,
        UnderReview,
        Resolved,
        Rejected
    }

    public enum MockTestType
    {
        FullMockTest,
        SubjectQuiz,
        TopicPractice,
        DailyQuiz
    }

    // SCORAM_TESTS: Practice Tests + Mock Tests share one attempt/result backbone
    // (StudentTestResult/StudentAnswer) -- this tells the two apart.
    public enum TestKind
    {
        Practice,
        Mock,
        // Previous Year Paper Practice: a real previous-year exam paper (Models/Paper.cs), attempted
        // through this same shared engine. A paper's questions come from Question.PaperId (existing
        // PYQ upload flow) UNION PaperQuestionBankLink (existing Question Bank questions mapped onto
        // this paper) -- see StudentTestResult.PaperId and Controllers/StudentPapersController.Start.
        PreviousYearPaper,
        // Weak Topics Quiz (Phase 1 of the "Quizzes" feature) -- short (5-20 question), zero-config,
        // auto-generated from the student's OWN wrong-answer history across every other TestKind (see
        // TestAttemptService.SelectWeakTopicQuestionsAsync). Deliberately not a Paper/MockTest/
        // PracticeTestTemplate row -- there's nothing an admin curates and nothing to "regenerate with
        // the same filters" the way Practice Tests work, it's recomputed fresh every time from
        // whatever the student is currently weak at. Falls back to a general mixed pool when a
        // student doesn't have enough graded history yet, so the feature is never a dead end for a
        // brand-new student.
        Quiz
    }

    // Lifecycle of a single student's attempt. Rows now exist from the moment a test is STARTED
    // (not just at submission, as before) so answers can be auto-saved and an interrupted attempt
    // can be resumed instead of losing all progress on a dropped connection.
    public enum TestAttemptStatus
    {
        InProgress,
        Submitted,
        AutoSubmitted,
        Expired
    }

    // Admin-facing lifecycle for a MockTest or PracticeTestTemplate -- Draft is only visible to
    // admins, Published is what students can see/start, Archived hides it from new attempts without
    // deleting it (deleting would corrupt historical attempts' references).
    public enum TestPublishStatus
    {
        Draft,
        Published,
        Archived
    }

    public enum UserLevel
    {
        Beginner,
        Intermediate,
        Expert,
        Master
    }

    public enum ReferralStatus
    {
        Pending,
        Joined,
        Rewarded
    }

    public enum SyllabusStatus
    {
        NotStarted,
        InProgress,
        Completed
    }

    public enum CurrentAffairsCategory
    {
        National,
        International,
        Sports,
        Awards,
        GovernmentSchemes
    }

    public enum TypingLanguage
    {
        Hindi,
        English
    }

    public enum ExamEventType
    {
        ExamDate,
        ApplicationDeadline,
        AdmitCard,
        Result
    }

    public enum AdminTaskStatus
    {
        Pending,
        InProgress,
        Completed
    }

    // SRS extension: PYQ papers now go through a review pipeline before students can see them.
    public enum PaperStatus
    {
        Draft,          // admin is still adding/editing questions
        PendingReview,  // admin submitted it, but doesn't have Publish permission themself
        Published       // visible to students
    }

    // Granular, per-admin capabilities (replaces "any Admin can do anything an Admin can do").
    // Deliberately a flat enum (not fixed boolean columns on Admin) so new capabilities can be added
    // later by adding an enum value, not a schema migration -- see AdminPermissionGrant.
    public enum AdminPermission
    {
        UploadPaper,
        EditPaper,
        DeletePaper,
        PublishPaper,
        Audit,
        // Group Chat moderation -- deliberately separate permissions rather than one big
        // "ModerateChat" flag, so e.g. an admin trusted to post exam notices doesn't automatically
        // also get the ability to remove students or read reported messages.
        RemoveGroupMembers,
        CreatePolls,
        PostNotices,
        ToggleChatLock,
        HandleChatReports,
        ManageBannedWords,
        ModerateSolutions,
        ModerateDiscussions,
        // Question Bank (SCORAM_QUESTION_BANK): separate from UploadPaper/EditPaper/DeletePaper on
        // purpose -- an admin trusted to manage the individual-question search bank (add/edit/delete/
        // bulk-import questions, manage Subjects/Topics) doesn't automatically also get the PYQ paper
        // upload/publish workflow, and vice versa.
        ManageQuestionBank,
        // Covers the "Report Question" review queue for BOTH legacy PYQ questions and Question Bank
        // questions (they share the QuestionReport table -- see Models/QuestionModels.cs). Kept
        // separate from ModerateSolutions since reviewing "this question is wrong" reports is a
        // different responsibility than approving alternative solving methods.
        ModerateQuestionReports,
        // SCORAM_TESTS: create/edit/publish/schedule Mock Tests and Practice Test templates, and
        // view student attempts/results. Separate from ManageQuestionBank -- an admin trusted to
        // curate the question pool doesn't automatically get to assemble/schedule graded tests from
        // it, and vice versa.
        ManageTests,
        // Create/edit/delete Chat Rooms themselves (standalone groups not tied to an Exam, e.g.
        // "Daily Doubt Room", plus renaming/deleting any room). Separate from the existing
        // moderation permissions (ToggleChatLock, RemoveGroupMembers, etc.), which govern day-to-day
        // moderation of a room that already exists, not the room's own lifecycle.
        ManageChatRooms
    }

    // Deliberately just these two for now, per product decision -- not the free-text Language field
    // question uploads originally used. Adding a third language later is a one-line enum addition.
    public enum PaperLanguage
    {
        Hindi,
        English
    }

    public enum ChatRoomPostPermission
    {
        AllMembers,
        AdminOnly
    }

    public enum ChatMessageType
    {
        Text,
        Image,
        Document,
        Poll,
        Notice,   // admin-posted announcement, shown highlighted/pinned in the room
        // A student re-sharing a Scoram Question Bank question into the room. See
        // ChatMessage.SharedQuestionBankQuestionId.
        QuestionShare
    }

    // Separate from ChatMessageType on purpose -- direct messages are a different table with no
    // admin/Poll/Notice concepts, and need Audio (voice notes) which room chat doesn't support.
    public enum DirectMessageType
    {
        Text,
        Image,
        Document,
        Audio,
        // A student re-sharing a Scoram Question Bank question into the DM. See
        // DirectMessage.SharedQuestionBankQuestionId.
        QuestionShare
    }

    public enum ChatReportStatus
    {
        Pending,
        ActionTaken,
        Dismissed
    }

    public enum NotificationType
    {
        Mention,
        DirectMessage
    }

    public enum ImportFileFormat
    {
        Csv,
        Excel,
        Json
    }

    public enum ImportJobStatus
    {
        PendingReview,
        Committed,
        RolledBack,
        Failed
    }
}
