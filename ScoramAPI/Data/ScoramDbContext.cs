using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using ScoramAPI.Models;

namespace ScoramAPI.Data
{
    public class ScoramDbContext : DbContext
    {
        public ScoramDbContext(DbContextOptions<ScoramDbContext> options) : base(options) { }

        // BUG FIX (SCORAM_TESTS live-attempt timer) -- SQL Server's datetime2 columns don't persist
        // DateTime.Kind. Every value we write is UTC (DateTime.UtcNow throughout the codebase), but
        // once EF Core reads a row back from the database, the materialized DateTime comes back with
        // Kind = Unspecified. System.Text.Json then serializes an Unspecified DateTime WITHOUT a "Z"
        // suffix (e.g. "2026-08-13T09:35:57" instead of "...Z"), so the frontend's `new Date(...)`
        // parses it as LOCAL time instead of UTC. On an IST machine that's a 5:30 offset -- which is
        // exactly why TestRunner's countdown was reading `expiresAt` as already having passed and
        // auto-submitting the attempt within the same second it was created (see StartedAt/ExpiresAt
        // on TestAttemptStartResponseDto, and every other DateTime column below).
        //
        // Fixing this globally here (rather than patching each DTO mapping) means every existing and
        // future DateTime/DateTime? column is protected the same way, not just the Tests module.
        protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
        {
            base.ConfigureConventions(configurationBuilder);

            configurationBuilder.Properties<DateTime>().HaveConversion<UtcDateTimeConverter>();
            configurationBuilder.Properties<DateTime?>().HaveConversion<NullableUtcDateTimeConverter>();
        }

        // Core
        public DbSet<User> Users => Set<User>();
        public DbSet<Admin> Admins => Set<Admin>();
        public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
        public DbSet<ImportJob> ImportJobs => Set<ImportJob>();
        public DbSet<Exam> Exams => Set<Exam>();
        public DbSet<Organization> Organizations => Set<Organization>();
        // "MY EXAMS" -- see Models/UserExamPreference.cs.
        public DbSet<UserExamPreference> UserExamPreferences => Set<UserExamPreference>();
        public DbSet<Question> Questions => Set<Question>();
        public DbSet<QuestionSolution> QuestionSolutions => Set<QuestionSolution>();
        public DbSet<QuestionReport> QuestionReports => Set<QuestionReport>();
        public DbSet<QuestionComment> QuestionComments => Set<QuestionComment>();
        public DbSet<CommentReport> CommentReports => Set<CommentReport>();
        public DbSet<CommentVote> CommentVotes => Set<CommentVote>();
        public DbSet<QuestionVote> QuestionVotes => Set<QuestionVote>();
        public DbSet<ChatRoom> ChatRooms => Set<ChatRoom>();
        public DbSet<ChatMessage> ChatMessages => Set<ChatMessage>();
        public DbSet<ChatRoomMembership> ChatRoomMemberships => Set<ChatRoomMembership>();
        public DbSet<ChatMessageMention> ChatMessageMentions => Set<ChatMessageMention>();
        public DbSet<ChatReport> ChatReports => Set<ChatReport>();
        public DbSet<ChatPoll> ChatPolls => Set<ChatPoll>();
        public DbSet<ChatPollOption> ChatPollOptions => Set<ChatPollOption>();
        public DbSet<ChatPollVote> ChatPollVotes => Set<ChatPollVote>();
        public DbSet<DirectConversation> DirectConversations => Set<DirectConversation>();
        public DbSet<DirectMessage> DirectMessages => Set<DirectMessage>();
        public DbSet<Notification> Notifications => Set<Notification>();
        public DbSet<PushSubscription> PushSubscriptions => Set<PushSubscription>();
        public DbSet<BannedWord> BannedWords => Set<BannedWord>();
        public DbSet<MockTest> MockTests => Set<MockTest>();
        public DbSet<MockTestQuestion> MockTestQuestions => Set<MockTestQuestion>();
        public DbSet<Quiz> Quizzes => Set<Quiz>();
        public DbSet<QuizQuestion> QuizQuestions => Set<QuizQuestion>();
        public DbSet<QuizChallenge> QuizChallenges => Set<QuizChallenge>();
        public DbSet<StudentTestResult> StudentTestResults => Set<StudentTestResult>();
        public DbSet<StudentAnswer> StudentAnswers => Set<StudentAnswer>();
        public DbSet<AdminTask> AdminTasks => Set<AdminTask>();
        public DbSet<Paper> Papers => Set<Paper>();
        // Previous Year Paper Practice -- existing Question Bank questions mapped onto a Paper.
        public DbSet<PaperQuestionBankLink> PaperQuestionBankLinks => Set<PaperQuestionBankLink>();
        public DbSet<AdminPermissionGrant> AdminPermissionGrants => Set<AdminPermissionGrant>();

        // Question Bank (SCORAM_QUESTION_BANK)
        public DbSet<QuestionBankSubject> QuestionBankSubjects => Set<QuestionBankSubject>();
        public DbSet<QuestionBankTopic> QuestionBankTopics => Set<QuestionBankTopic>();
        public DbSet<QuestionBankQuestion> QuestionBankQuestions => Set<QuestionBankQuestion>();
        public DbSet<QuestionBankExamMapping> QuestionBankExamMappings => Set<QuestionBankExamMapping>();
        public DbSet<QuestionBankImportJob> QuestionBankImportJobs => Set<QuestionBankImportJob>();

        // Tests -- Practice Tests + Mock Tests (SCORAM_TESTS)
        public DbSet<PracticeTestTemplate> PracticeTestTemplates => Set<PracticeTestTemplate>();
        public DbSet<PracticeTestTemplateQuestion> PracticeTestTemplateQuestions => Set<PracticeTestTemplateQuestion>();

        // Gamification & Engagement (New)
        public DbSet<UserStreak> UserStreaks => Set<UserStreak>();
        public DbSet<UserXP> UserXPs => Set<UserXP>();
        public DbSet<Badge> Badges => Set<Badge>();
        public DbSet<UserBadge> UserBadges => Set<UserBadge>();
        public DbSet<Referral> Referrals => Set<Referral>();
        public DbSet<XpTransaction> XpTransactions => Set<XpTransaction>();
        public DbSet<UserQuestionSolve> UserQuestionSolves => Set<UserQuestionSolve>();

        // Exam Utility (New)
        public DbSet<SyllabusTopic> SyllabusTopics => Set<SyllabusTopic>();
        public DbSet<StudentSyllabusProgress> StudentSyllabusProgress => Set<StudentSyllabusProgress>();
        public DbSet<CurrentAffair> CurrentAffairs => Set<CurrentAffair>();
        public DbSet<TypingTestResult> TypingTestResults => Set<TypingTestResult>();
        public DbSet<ExamCalendarEvent> ExamCalendarEvents => Set<ExamCalendarEvent>();
        public DbSet<JobAlert> JobAlerts => Set<JobAlert>();

        // Azure Blob Storage -- file metadata only, actual bytes live in the "uploads" container.
        public DbSet<Document> Documents => Set<Document>();
        public DbSet<Bookmark> Bookmarks => Set<Bookmark>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // ---------- Unique constraints ----------
            modelBuilder.Entity<User>()
                .HasIndex(u => u.Email)
                .IsUnique();

            modelBuilder.Entity<User>()
                .HasIndex(u => u.Username)
                .IsUnique();

            modelBuilder.Entity<User>()
                .HasIndex(u => u.PhoneNumber)
                .IsUnique();

            // Explicit HasDefaultValue(true) rather than relying on the C# property initializer --
            // that only covers newly-inserted rows. This also backfills every existing user to "on"
            // when the migration runs, matching "notifications are on by default" for the whole base.
            modelBuilder.Entity<User>().Property(u => u.NotifyOnGroupMessages).HasDefaultValue(true);
            modelBuilder.Entity<User>().Property(u => u.NotifyOnDirectMessages).HasDefaultValue(true);

            modelBuilder.Entity<Admin>()
                .HasIndex(a => a.Email)
                .IsUnique();

            // GAMIFICATION -- ReferralCode here is a *copy* of the referrer's permanent User.ReferralCode
            // (see User.cs), written onto every successful-referral row, so it is deliberately NOT
            // unique at this table's level -- many rows share one referrer's code. Non-unique index
            // kept only for lookup speed (e.g. admin auditing "who used code X").
            modelBuilder.Entity<Referral>()
                .HasIndex(r => r.ReferralCode);

            modelBuilder.Entity<User>()
                .HasIndex(u => u.ReferralCode)
                .IsUnique()
                .HasFilter("[ReferralCode] IS NOT NULL"); // most users never generate one -- many nulls must coexist

            modelBuilder.Entity<UserStreak>()
                .HasIndex(s => s.UserId)
                .IsUnique();

            modelBuilder.Entity<UserXP>()
                .HasIndex(x => x.UserId)
                .IsUnique();

            // One solve record per (student, question) -- re-visiting a question you've already
            // solved doesn't grant XP/streak credit again.
            modelBuilder.Entity<UserQuestionSolve>()
                .HasIndex(s => new { s.UserId, s.QuestionBankQuestionId })
                .IsUnique();

            // Leaderboard queries filter by CreatedAt range (weekly/monthly) and group by UserId, and
            // exam-wise leaderboards additionally filter by ExamName -- both as composite indexes so
            // neither query pattern falls back to a full table scan as XpTransactions grows.
            modelBuilder.Entity<XpTransaction>()
                .HasIndex(t => new { t.UserId, t.CreatedAt });
            modelBuilder.Entity<XpTransaction>()
                .HasIndex(t => new { t.ExamName, t.CreatedAt });

            // ---------- Enum -> string conversions (readable in DB) ----------
            modelBuilder.Entity<Admin>().Property(a => a.Role).HasConversion<string>().HasMaxLength(20);
            modelBuilder.Entity<Question>().Property(q => q.DifficultyLevel).HasConversion<string>().HasMaxLength(20);
            modelBuilder.Entity<Question>().Property(q => q.CorrectOption).HasConversion<string>().HasMaxLength(5);

            modelBuilder.Entity<Exam>().HasIndex(e => e.Name).IsUnique();

            // ORGANIZATION HIERARCHY -- see Exam.OrganizationId's own comment. Restrict (not
            // Cascade/SetNull): OrganizationsController's own delete guard already refuses to
            // hard-delete an Organization that still has exams attached (same "Block, not Delete"
            // pattern ExamsController.Delete uses), so this only ever matters as a safety net.
            modelBuilder.Entity<Exam>()
                .HasOne(e => e.Organization)
                .WithMany(o => o.Exams)
                .HasForeignKey(e => e.OrganizationId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<Organization>().HasIndex(o => o.Name).IsUnique();

            modelBuilder.Entity<Question>()
                .HasOne(q => q.Exam)
                .WithMany(e => e.Questions)
                .HasForeignKey(q => q.ExamId)
                .OnDelete(DeleteBehavior.Restrict);
            modelBuilder.Entity<QuestionSolution>().Property(s => s.SolutionType).HasConversion<string>().HasMaxLength(30);
            modelBuilder.Entity<QuestionReport>().Property(r => r.ReportType).HasConversion<string>().HasMaxLength(30);
            modelBuilder.Entity<QuestionReport>().Property(r => r.Status).HasConversion<string>().HasMaxLength(20);
            modelBuilder.Entity<MockTest>().Property(m => m.TestType).HasConversion<string>().HasMaxLength(20);
            modelBuilder.Entity<MockTest>().Property(m => m.Status).HasConversion<string>().HasMaxLength(20);
            // "MY EXAMS" -- nullable FK alongside the pre-existing free-text ExamName (see
            // MockTest.ExamId's own comment in Models/MockTestModels.cs). Restrict, same reasoning
            // as every other student-facing Exam FK in this file.
            modelBuilder.Entity<MockTest>()
                .HasOne(m => m.Exam)
                .WithMany()
                .HasForeignKey(m => m.ExamId)
                .OnDelete(DeleteBehavior.Restrict);
            modelBuilder.Entity<Quiz>().Property(q => q.Status).HasConversion<string>().HasMaxLength(20);
            modelBuilder.Entity<QuizChallenge>().Property(c => c.Status).HasConversion<string>().HasMaxLength(20);
            modelBuilder.Entity<StudentAnswer>().Property(a => a.CorrectOptionSnapshot).HasConversion<string>().HasMaxLength(5);
            modelBuilder.Entity<StudentTestResult>().Property(r => r.TestKind).HasConversion<string>().HasMaxLength(20);
            modelBuilder.Entity<StudentTestResult>().Property(r => r.Status).HasConversion<string>().HasMaxLength(20);
            modelBuilder.Entity<StudentTestResult>().Property(r => r.PracticeDifficulty).HasConversion<string>().HasMaxLength(20);
            modelBuilder.Entity<PracticeTestTemplate>().Property(t => t.Difficulty).HasConversion<string>().HasMaxLength(20);
            modelBuilder.Entity<PracticeTestTemplate>().Property(t => t.Status).HasConversion<string>().HasMaxLength(20);
            modelBuilder.Entity<PracticeTestTemplate>().Property(t => t.NegativeMarkingRatio).HasPrecision(4, 2);
            modelBuilder.Entity<UserXP>().Property(x => x.CurrentLevel).HasConversion<string>().HasMaxLength(20);
            modelBuilder.Entity<Referral>().Property(r => r.Status).HasConversion<string>().HasMaxLength(20);
            modelBuilder.Entity<StudentSyllabusProgress>().Property(p => p.Status).HasConversion<string>().HasMaxLength(20);
            modelBuilder.Entity<CurrentAffair>().Property(c => c.Category).HasConversion<string>().HasMaxLength(30);
            modelBuilder.Entity<TypingTestResult>().Property(t => t.Language).HasConversion<string>().HasMaxLength(10);
            modelBuilder.Entity<ExamCalendarEvent>().Property(e => e.EventType).HasConversion<string>().HasMaxLength(30);
            modelBuilder.Entity<AdminTask>().Property(t => t.Status).HasConversion<string>().HasMaxLength(20);
            modelBuilder.Entity<StudentAnswer>().Property(a => a.SelectedOption).HasConversion<string>().HasMaxLength(5);
            modelBuilder.Entity<Paper>().Property(p => p.Status).HasConversion<string>().HasMaxLength(20);
            modelBuilder.Entity<Paper>().Property(p => p.Language).HasConversion<string>().HasMaxLength(10);
            modelBuilder.Entity<AdminPermissionGrant>().Property(g => g.Permission).HasConversion<string>().HasMaxLength(30);
            modelBuilder.Entity<Notification>().Property(n => n.Type).HasConversion<string>().HasMaxLength(20);
            modelBuilder.Entity<ChatMessage>().Property(m => m.MessageType).HasConversion<string>().HasMaxLength(20);
            modelBuilder.Entity<DirectMessage>().Property(m => m.MessageType).HasConversion<string>().HasMaxLength(20);
            modelBuilder.Entity<ChatReport>().Property(r => r.Status).HasConversion<string>().HasMaxLength(20);

            // ---------- Decimal precision ----------
            modelBuilder.Entity<MockTest>().Property(m => m.NegativeMarkingRatio).HasPrecision(4, 2);
            modelBuilder.Entity<Quiz>().Property(q => q.NegativeMarkingRatio).HasPrecision(4, 2);
            modelBuilder.Entity<StudentTestResult>().Property(r => r.Score).HasPrecision(6, 2);
            // Matches MockTest/Quiz's own precision (4,2) -- this is where their NegativeMarkingRatio
            // gets snapshotted onto the attempt at start time, so it should hold the same range.
            modelBuilder.Entity<StudentTestResult>().Property(r => r.NegativeMarkingRatio).HasPrecision(4, 2);
            modelBuilder.Entity<TypingTestResult>().Property(t => t.WPM).HasPrecision(5, 2);
            modelBuilder.Entity<TypingTestResult>().Property(t => t.Accuracy).HasPrecision(5, 2);

            // ---------- Restrict cascade deletes where it would create multiple cascade paths ----------
            modelBuilder.Entity<QuestionSolution>()
                .HasOne(s => s.SubmittedByUser)
                .WithMany(u => u.QuestionSolutions)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<QuestionSolution>()
                .HasOne(s => s.SubmittedByAdmin)
                .WithMany()
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<QuestionComment>()
                .HasOne(c => c.ParentComment)
                .WithMany()
                .HasForeignKey(c => c.ParentCommentId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<QuestionComment>()
                .HasOne(c => c.SubmittedByAdmin)
                .WithMany()
                .HasForeignKey(c => c.SubmittedByAdminId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<CommentReport>()
                .HasOne(r => r.Comment)
                .WithMany()
                .HasForeignKey(r => r.CommentId)
                .OnDelete(DeleteBehavior.Cascade); // a deleted comment takes its own reports with it

            modelBuilder.Entity<CommentReport>()
                .HasOne(r => r.ReportedByUser)
                .WithMany()
                .HasForeignKey(r => r.ReportedByUserId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<MockTestQuestion>()
                .HasOne(mq => mq.Question)
                .WithMany(q => q.MockTestQuestions)
                .HasForeignKey(mq => mq.QuestionId)
                .OnDelete(DeleteBehavior.Restrict);

            // SCORAM_TESTS: a MockTest paper can mix legacy PYQ and Question Bank questions --
            // same dual-FK reuse pattern as Solutions/Reports/Comments/Votes.
            modelBuilder.Entity<MockTestQuestion>()
                .HasOne(mq => mq.QuestionBankQuestion)
                .WithMany()
                .HasForeignKey(mq => mq.QuestionBankQuestionId)
                .OnDelete(DeleteBehavior.Restrict);

            // Quizzes (Phase 2, admin-curated Daily Quiz) -- Question Bank only, no legacy-Question
            // FK needed (see QuizQuestion's own comment).
            modelBuilder.Entity<QuizQuestion>()
                .HasOne(qq => qq.QuestionBankQuestion)
                .WithMany()
                .HasForeignKey(qq => qq.QuestionBankQuestionId)
                .OnDelete(DeleteBehavior.Restrict);

            // Quiz Challenges (Phase 3) -- four separate FKs converging on just two tables (User,
            // StudentTestResult). ALL FOUR need Restrict, not just three of them -- SQL Server
            // refuses to even create the schema if more than one cascade path could reach the same
            // table, and every other FK in this file already defaults to Restrict for exactly this
            // reason, so this isn't a special case, just consistency.
            modelBuilder.Entity<QuizChallenge>()
                .HasOne(c => c.ChallengerUser)
                .WithMany()
                .HasForeignKey(c => c.ChallengerUserId)
                .OnDelete(DeleteBehavior.Restrict);
            modelBuilder.Entity<QuizChallenge>()
                .HasOne(c => c.ChallengedUser)
                .WithMany()
                .HasForeignKey(c => c.ChallengedUserId)
                .OnDelete(DeleteBehavior.Restrict);
            modelBuilder.Entity<QuizChallenge>()
                .HasOne(c => c.SourceAttempt)
                .WithMany()
                .HasForeignKey(c => c.SourceAttemptId)
                .OnDelete(DeleteBehavior.Restrict);
            modelBuilder.Entity<QuizChallenge>()
                .HasOne(c => c.ChallengedAttempt)
                .WithMany()
                .HasForeignKey(c => c.ChallengedAttemptId)
                .OnDelete(DeleteBehavior.Restrict);
            // Fast "my pending challenges" lookup -- the one query this feature actually needs to be
            // quick, everything else here is low-volume enough not to matter.
            modelBuilder.Entity<QuizChallenge>()
                .HasIndex(c => new { c.ChallengedUserId, c.Status });
            modelBuilder.Entity<QuizChallenge>()
                .HasIndex(c => c.ChallengerUserId);
            modelBuilder.Entity<QuizChallenge>()
                .HasIndex(c => c.BatchId);

            modelBuilder.Entity<StudentAnswer>()
                .HasOne(a => a.Question)
                .WithMany()
                .HasForeignKey(a => a.QuestionId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<StudentAnswer>()
                .HasOne(a => a.QuestionBankQuestion)
                .WithMany()
                .HasForeignKey(a => a.QuestionBankQuestionId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<StudentTestResult>()
                .HasOne(r => r.MockTest)
                .WithMany(m => m.Results)
                .HasForeignKey(r => r.MockTestId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<StudentTestResult>()
                .HasOne(r => r.PracticeTestTemplate)
                .WithMany()
                .HasForeignKey(r => r.PracticeTestTemplateId)
                .OnDelete(DeleteBehavior.Restrict);

            // Previous Year Paper Practice -- same reasoning as MockTest/PracticeTestTemplate above:
            // an attempt references its Paper but a Paper is never deleted out from under a
            // historical attempt without that being an explicit, deliberate admin action elsewhere.
            modelBuilder.Entity<StudentTestResult>()
                .HasOne(r => r.Paper)
                .WithMany()
                .HasForeignKey(r => r.PaperId)
                .OnDelete(DeleteBehavior.Restrict);

            // Quizzes (Phase 2) -- same reasoning again, for an attempt's link back to its
            // admin-curated Quiz (null for a Phase 1 Weak Topics Quiz attempt).
            modelBuilder.Entity<StudentTestResult>()
                .HasOne(r => r.Quiz)
                .WithMany(q => q.Results)
                .HasForeignKey(r => r.QuizId)
                .OnDelete(DeleteBehavior.Restrict);

            // A student can only have one IN-PROGRESS attempt at a time for a given MockTest (Practice
            // attempts are exempt -- MockTestId is null for those, so this filtered index doesn't apply
            // to them, and re-generating a fresh ad-hoc/template practice attempt any time is fine).
            // This is what makes "resume" well-defined: if a row already exists, resume it instead of
            // silently creating a second one.
            modelBuilder.Entity<StudentTestResult>()
                .HasIndex(r => new { r.UserId, r.MockTestId, r.Status })
                .HasFilter("[MockTestId] IS NOT NULL AND [Status] = 'InProgress'")
                .IsUnique();

            // Same "one in-progress attempt per paper" rule as MockTest above, for Previous Year
            // Paper Practice attempts.
            modelBuilder.Entity<StudentTestResult>()
                .HasIndex(r => new { r.UserId, r.PaperId, r.Status })
                .HasFilter("[PaperId] IS NOT NULL AND [Status] = 'InProgress'")
                .IsUnique();

            // Same rule again for a Quiz (Phase 2) attempt -- a Weak Topics Quiz (Phase 1, QuizId
            // null) is exempt, same as an ad-hoc Practice attempt is exempt from the MockTest index
            // above; regenerating a fresh Weak Topics Quiz any time is fine.
            modelBuilder.Entity<StudentTestResult>()
                .HasIndex(r => new { r.UserId, r.QuizId, r.Status })
                .HasFilter("[QuizId] IS NOT NULL AND [Status] = 'InProgress'")
                .IsUnique();

            modelBuilder.Entity<StudentAnswer>()
                .HasIndex(a => new { a.StudentTestResultId, a.QuestionOrder });

            // ==========================================================================
            // Practice Test templates (SCORAM_TESTS)
            // ==========================================================================

            modelBuilder.Entity<PracticeTestTemplate>()
                .HasOne(t => t.Subject)
                .WithMany()
                .HasForeignKey(t => t.SubjectId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<PracticeTestTemplate>()
                .HasOne(t => t.Topic)
                .WithMany()
                .HasForeignKey(t => t.TopicId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<PracticeTestTemplate>()
                .HasOne(t => t.Exam)
                .WithMany()
                .HasForeignKey(t => t.ExamId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<PracticeTestTemplate>()
                .HasOne(t => t.CreatedByAdmin)
                .WithMany()
                .HasForeignKey(t => t.CreatedByAdminId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<PracticeTestTemplateQuestion>()
                .HasOne(q => q.PracticeTestTemplate)
                .WithMany(t => t.Questions)
                .HasForeignKey(q => q.PracticeTestTemplateId)
                .OnDelete(DeleteBehavior.Cascade); // deleting a template takes its fixed question list with it

            modelBuilder.Entity<PracticeTestTemplateQuestion>()
                .HasOne(q => q.Question)
                .WithMany()
                .HasForeignKey(q => q.QuestionId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<PracticeTestTemplateQuestion>()
                .HasOne(q => q.QuestionBankQuestion)
                .WithMany()
                .HasForeignKey(q => q.QuestionBankQuestionId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<Referral>()
                .HasOne(r => r.ReferrerUser)
                .WithMany()
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<Referral>()
                .HasOne(r => r.ReferredUser)
                .WithMany()
                .OnDelete(DeleteBehavior.Restrict);

            // AdminTask has two FKs to Admin (AssignedToAdmin, AssignedByAdmin). Leaving both as the
            // EF Core default (Cascade) would give SQL Server two cascade paths into the same table,
            // which it rejects at migration time -- so the "assigned by" side is Restrict instead.
            modelBuilder.Entity<AdminTask>()
                .HasOne(t => t.AssignedToAdmin)
                .WithMany(a => a.AssignedTasks)
                .HasForeignKey(t => t.AssignedToAdminId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<AdminTask>()
                .HasOne(t => t.AssignedByAdmin)
                .WithMany()
                .HasForeignKey(t => t.AssignedByAdminId)
                .OnDelete(DeleteBehavior.Restrict);

            // ---------- Paper / permission system ----------
            modelBuilder.Entity<Paper>()
                .HasOne(p => p.Exam)
                .WithMany(e => e.Papers)
                .HasForeignKey(p => p.ExamId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<Paper>()
                .HasOne(p => p.CreatedByAdmin)
                .WithMany(a => a.UploadedPapers)
                .HasForeignKey(p => p.CreatedByAdminId)
                .OnDelete(DeleteBehavior.Restrict);

            // ---------- Audit log ----------
            // No inverse collection on Admin (a growing accountability trail isn't something the rest
            // of the app ever needs to navigate to from an Admin instance) -- WithMany() with no
            // argument gives EF Core a shadow inverse navigation instead of requiring one on the model.
            modelBuilder.Entity<AuditLog>()
                .HasOne(a => a.Admin)
                .WithMany()
                .HasForeignKey(a => a.AdminId)
                .OnDelete(DeleteBehavior.Restrict);

            // ---------- Bulk import ----------
            modelBuilder.Entity<ImportJob>()
                .HasOne(j => j.Paper)
                .WithMany()
                .HasForeignKey(j => j.PaperId)
                .OnDelete(DeleteBehavior.Cascade); // deleting a paper takes its import history with it

            modelBuilder.Entity<ImportJob>()
                .HasOne(j => j.CreatedByAdmin)
                .WithMany()
                .HasForeignKey(j => j.CreatedByAdminId)
                .OnDelete(DeleteBehavior.Restrict);

            // Restrict, not SetNull: SQL Server rejected SetNull here with "may cause cycles or
            // multiple cascade paths" -- deleting a Paper would cascade to Questions directly AND
            // cascade to ImportJobs, which would then need to SetNull back onto the very Questions
            // rows already being deleted by the first path. Restrict breaks that ambiguity. Safe in
            // practice because the app never deletes an ImportJob row directly (Rollback deletes the
            // Questions it created and just updates the ImportJob's Status to RolledBack) -- so this
            // constraint's NO ACTION is never actually exercised in normal use.
            modelBuilder.Entity<Question>()
                .HasOne(q => q.ImportJob)
                .WithMany()
                .HasForeignKey(q => q.ImportJobId)
                .OnDelete(DeleteBehavior.Restrict);

            // Non-unique -- fast lookups for the duplicate-paper check and the "Uploaded Papers" list.
            // PaperCode/Tier/ExamDate/Shift/PaperLabel aren't part of this index because the exact-match
            // duplicate check (which does include all of them) is application-level, not a DB
            // constraint -- see PapersController.Create.
            modelBuilder.Entity<Paper>()
                .HasIndex(p => new { p.ExamId, p.Year, p.Language });

            // Deleting a Paper deletes every Question under it -- this is exactly what "Delete Paper"
            // (to allow a fresh re-upload) means. Legacy Questions with PaperId = null are unaffected.
            modelBuilder.Entity<Question>()
                .HasOne(q => q.Paper)
                .WithMany(p => p.Questions)
                .HasForeignKey(q => q.PaperId)
                .OnDelete(DeleteBehavior.Cascade);

            // Catches accidental double-entry of the same Q.No within one paper. SQL Server allows
            // multiple NULLs through a composite unique index, so legacy rows with PaperId = null
            // (created before Paper existed) are unaffected by this constraint.
            modelBuilder.Entity<Question>()
                .HasIndex(q => new { q.PaperId, q.QuestionNumber })
                .IsUnique();

            // ---------- Previous Year Paper Practice ----------
            modelBuilder.Entity<Paper>().Property(p => p.NegativeMarkingRatio).HasPrecision(5, 2);

            modelBuilder.Entity<PaperQuestionBankLink>()
                .HasOne(l => l.Paper)
                .WithMany(p => p.QuestionBankLinks)
                .HasForeignKey(l => l.PaperId)
                .OnDelete(DeleteBehavior.Cascade); // deleting a Paper takes its QB mappings with it, same as its own Questions

            modelBuilder.Entity<PaperQuestionBankLink>()
                .HasOne(l => l.QuestionBankQuestion)
                .WithMany()
                .HasForeignKey(l => l.QuestionBankQuestionId)
                .OnDelete(DeleteBehavior.Restrict); // never cascade-delete a live Question Bank question because a paper referenced it

            modelBuilder.Entity<PaperQuestionBankLink>()
                .HasOne(l => l.LinkedByAdmin)
                .WithMany()
                .HasForeignKey(l => l.LinkedByAdminId)
                .OnDelete(DeleteBehavior.Restrict);

            // Same double-entry protection as Question's own (PaperId, QuestionNumber) index above,
            // for the Question-Bank side of a paper's question list.
            modelBuilder.Entity<PaperQuestionBankLink>()
                .HasIndex(l => new { l.PaperId, l.QuestionNumber })
                .IsUnique();

            // Prevents mapping the same Question Bank question into the same paper twice (spec
            // section 15, "Duplicacy Prevention").
            modelBuilder.Entity<PaperQuestionBankLink>()
                .HasIndex(l => new { l.PaperId, l.QuestionBankQuestionId })
                .IsUnique();

            modelBuilder.Entity<AdminPermissionGrant>()
                .HasOne(g => g.Admin)
                .WithMany(a => a.PermissionGrants)
                .HasForeignKey(g => g.AdminId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<AdminPermissionGrant>()
                .HasIndex(g => new { g.AdminId, g.Permission })
                .IsUnique();

            // ---------- Group Chat ----------
            modelBuilder.Entity<ChatRoom>()
                .HasOne(r => r.Exam)
                .WithMany()
                .HasForeignKey(r => r.ExamId)
                .OnDelete(DeleteBehavior.Restrict);

            // One room per exam -- this is what ExamsController.Create's auto-create check relies on.
            // Filtered so it only applies to Exam-linked rooms; many standalone rooms (ExamId == null,
            // e.g. "Daily Doubt Room") can coexist without tripping this constraint.
            modelBuilder.Entity<ChatRoom>()
                .HasIndex(r => r.ExamId)
                .IsUnique()
                .HasFilter("[ExamId] IS NOT NULL");

            modelBuilder.Entity<ChatMessage>()
                .HasOne(m => m.SharedQuestionBankQuestion)
                .WithMany()
                .HasForeignKey(m => m.SharedQuestionBankQuestionId)
                .OnDelete(DeleteBehavior.SetNull); // a shared message survives the source question being deleted later

            modelBuilder.Entity<ChatRoomMembership>()
                .HasOne(m => m.ChatRoom)
                .WithMany(r => r.Memberships)
                .HasForeignKey(m => m.ChatRoomId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<ChatRoomMembership>()
                .HasOne(m => m.User)
                .WithMany()
                .HasForeignKey(m => m.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<ChatRoomMembership>()
                .HasOne(m => m.BannedByAdmin)
                .WithMany()
                .HasForeignKey(m => m.BannedByAdminId)
                .OnDelete(DeleteBehavior.Restrict);

            // A user has at most one membership row per room -- re-joining just flips IsBanned back to
            // false on the same row rather than creating a duplicate.
            modelBuilder.Entity<ChatRoomMembership>()
                .HasIndex(m => new { m.ChatRoomId, m.UserId })
                .IsUnique();

            modelBuilder.Entity<ChatMessage>()
                .HasOne(m => m.ChatRoom)
                .WithMany(r => r.Messages)
                .HasForeignKey(m => m.ChatRoomId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<ChatMessage>()
                .HasOne(m => m.User)
                .WithMany()
                .HasForeignKey(m => m.UserId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<ChatMessage>()
                .HasOne(m => m.SenderAdmin)
                .WithMany()
                .HasForeignKey(m => m.SenderAdminId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<ChatMessage>()
                .HasOne(m => m.Poll)
                .WithMany()
                .HasForeignKey(m => m.PollId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<ChatMessageMention>()
                .HasOne(m => m.ChatMessage)
                .WithMany(msg => msg.Mentions)
                .HasForeignKey(m => m.ChatMessageId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<ChatMessageMention>()
                .HasOne(m => m.MentionedUser)
                .WithMany()
                .HasForeignKey(m => m.MentionedUserId)
                .OnDelete(DeleteBehavior.Restrict);

            // Fast "give me my unread mentions" lookups.
            modelBuilder.Entity<ChatMessageMention>()
                .HasIndex(m => new { m.MentionedUserId, m.IsRead });

            modelBuilder.Entity<ChatReport>()
                .HasOne(r => r.ChatMessage)
                .WithMany(msg => msg.Reports)
                .HasForeignKey(r => r.ChatMessageId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<ChatReport>()
                .HasOne(r => r.ReportedByUser)
                .WithMany()
                .HasForeignKey(r => r.ReportedByUserId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<ChatReport>()
                .HasOne(r => r.ResolvedByAdmin)
                .WithMany()
                .HasForeignKey(r => r.ResolvedByAdminId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<ChatPoll>()
                .HasOne(p => p.ChatRoom)
                .WithMany(r => r.Polls)
                .HasForeignKey(p => p.ChatRoomId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<ChatPoll>()
                .HasOne(p => p.CreatedByAdmin)
                .WithMany()
                .HasForeignKey(p => p.CreatedByAdminId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<ChatPollOption>()
                .HasOne(o => o.ChatPoll)
                .WithMany(p => p.Options)
                .HasForeignKey(o => o.ChatPollId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<ChatPollVote>()
                .HasOne(v => v.ChatPollOption)
                .WithMany(o => o.Votes)
                .HasForeignKey(v => v.ChatPollOptionId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<ChatPollVote>()
                .HasOne(v => v.User)
                .WithMany()
                .HasForeignKey(v => v.UserId)
                .OnDelete(DeleteBehavior.Restrict);

            // Prevents voting the same option twice; "single choice" vs "multiple choice" (whether a
            // user can vote on >1 *different* options of the same poll) is enforced in application code.
            modelBuilder.Entity<ChatPollVote>()
                .HasIndex(v => new { v.ChatPollOptionId, v.UserId })
                .IsUnique();

            // Both FKs point at the same Users table -- Restrict (not Cascade) on both to avoid SQL
            // Server's multiple-cascade-paths error, same reasoning as ChatMessage.User below.
            modelBuilder.Entity<DirectConversation>()
                .HasOne(c => c.UserA)
                .WithMany()
                .HasForeignKey(c => c.UserAId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<DirectConversation>()
                .HasOne(c => c.UserB)
                .WithMany()
                .HasForeignKey(c => c.UserBId)
                .OnDelete(DeleteBehavior.Restrict);

            // UserAId is always the smaller Guid (see DirectMessagesController) so a given pair of
            // users can only ever have one conversation row, regardless of who messaged first.
            modelBuilder.Entity<DirectConversation>()
                .HasIndex(c => new { c.UserAId, c.UserBId })
                .IsUnique();

            // Fast "give me my conversations sorted by most recent" lookups.
            modelBuilder.Entity<DirectConversation>()
                .HasIndex(c => c.LastMessageAt);

            modelBuilder.Entity<DirectMessage>()
                .HasOne(m => m.Conversation)
                .WithMany(c => c.Messages)
                .HasForeignKey(m => m.ConversationId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<DirectMessage>()
                .HasOne(m => m.Sender)
                .WithMany()
                .HasForeignKey(m => m.SenderId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<DirectMessage>()
                .HasOne(m => m.SharedQuestionBankQuestion)
                .WithMany()
                .HasForeignKey(m => m.SharedQuestionBankQuestionId)
                .OnDelete(DeleteBehavior.SetNull);

            // Fast "give me this conversation's messages, newest first" pagination.
            modelBuilder.Entity<DirectMessage>()
                .HasIndex(m => new { m.ConversationId, m.SentAt });

            modelBuilder.Entity<Notification>()
                .HasOne(n => n.User)
                .WithMany()
                .HasForeignKey(n => n.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            // Fast "give me this user's notifications, newest first" + fast unread-count lookups.
            modelBuilder.Entity<Notification>()
                .HasIndex(n => new { n.UserId, n.CreatedAt });
            modelBuilder.Entity<Notification>()
                .HasIndex(n => new { n.UserId, n.IsRead });

            modelBuilder.Entity<PushSubscription>()
                .HasOne(p => p.User)
                .WithMany()
                .HasForeignKey(p => p.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            // A given browser endpoint should only ever be stored once -- re-subscribing (e.g. after
            // clearing site data) just updates the existing row instead of accumulating duplicates.
            modelBuilder.Entity<PushSubscription>()
                .HasIndex(p => p.Endpoint)
                .IsUnique();

            modelBuilder.Entity<BannedWord>()
                .HasOne(w => w.AddedByAdmin)
                .WithMany()
                .HasForeignKey(w => w.AddedByAdminId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<BannedWord>()
                .HasIndex(w => w.Word)
                .IsUnique();

            // ---------- Seed a few default badges & chat rooms so the app is usable immediately ----------
            // NOTE: HasData requires fixed, hardcoded key values (not Guid.NewGuid()) so that EF can compute
            // a stable migration diff -- these are arbitrary fixed GUIDs, not meaningful data.
            modelBuilder.Entity<Badge>().HasData(
                new Badge { Id = Guid.Parse("8f14e45f-ceea-467e-add1-000000000001"), Name = "10-Second Trick Master", Description = "Submitted a verified shortcut solution", CriteriaDescription = "1 verified shortcut solution" },
                new Badge { Id = Guid.Parse("8f14e45f-ceea-467e-add1-000000000002"), Name = "Top Contributor", Description = "Highly upvoted community solutions", CriteriaDescription = "50+ upvotes across solutions" },
                new Badge { Id = Guid.Parse("8f14e45f-ceea-467e-add1-000000000003"), Name = "100 Day Warrior", Description = "Maintained a 100 day streak", CriteriaDescription = "100 day streak" },
                new Badge { Id = Guid.Parse("8f14e45f-ceea-467e-add1-000000000004"), Name = "Verified Solver", Description = "Solution marked verified by admin", CriteriaDescription = "1 admin-verified solution" }
            );

            // ---------- Seed the first Super Admin (bootstrap account) ----------
            // There's no public admin self-registration by design (SRS: only a Super Admin creates
            // other admins). Since HasData needs a fixed value, this password hash is a pre-computed
            // BCrypt hash of "SuperAdmin@123" -- change this password immediately after first login via
            // a future "change password" endpoint, or update the hash here before your first deployment.
            modelBuilder.Entity<Admin>().HasData(
                new Admin
                {
                    Id = Guid.Parse("a1b2c3d4-0000-4000-8000-000000000001"),
                    FullName = "Super Admin",
                    Email = "superadmin@scoram.com",
                    PasswordHash = "$2b$10$iHMto/L2wJaon4hjWIC8CeZNXGiQ3Fe4wMpa8tGvi9jybrHnSPqHa",
                    Role = ScoramAPI.Enums.AdminRole.SuperAdmin,
                    IsActive = true,
                    CreatedAt = new DateTime(2026, 7, 1)
                }
            );

            // Chat rooms are now created automatically per-Exam (see ExamsController.Create and the
            // admin "sync rooms" endpoint for backfilling exams that predate this feature) rather than
            // seeded as fixed placeholder rooms here.

            // ==========================================================================
            // Question Bank (SCORAM_QUESTION_BANK)
            // ==========================================================================

            modelBuilder.Entity<QuestionBankQuestion>().Property(q => q.CorrectOption).HasConversion<string>().HasMaxLength(5);
            // Nullable, unlike Paper.Language above -- see the model's own comment on why (older
            // rows predate this column). Same HasMaxLength(10) as Paper.Language for consistency.
            modelBuilder.Entity<QuestionBankQuestion>().Property(q => q.Language).HasConversion<string>().HasMaxLength(10);
            modelBuilder.Entity<QuestionBankImportJob>().Property(j => j.Format).HasConversion<string>().HasMaxLength(20);
            modelBuilder.Entity<QuestionBankImportJob>().Property(j => j.Status).HasConversion<string>().HasMaxLength(20);

            // A Subject name is unique among ACTIVE subjects only (partial/filtered index) -- a
            // retired ("deactivated") subject's name doesn't permanently block reusing that name.
            // Topic uniqueness is scoped to its Subject (two different subjects can each have a
            // "Basics" topic) via a composite index instead.
            modelBuilder.Entity<QuestionBankSubject>()
                .HasIndex(s => s.Name)
                .IsUnique();

            modelBuilder.Entity<QuestionBankTopic>()
                .HasIndex(t => new { t.SubjectId, t.Name })
                .IsUnique();

            modelBuilder.Entity<QuestionBankTopic>()
                .HasOne(t => t.Subject)
                .WithMany(s => s.Topics)
                .HasForeignKey(t => t.SubjectId)
                .OnDelete(DeleteBehavior.Restrict); // don't let a Subject delete silently orphan/cascade its Topics

            modelBuilder.Entity<QuestionBankQuestion>()
                .HasOne(q => q.Subject)
                .WithMany(s => s.Questions)
                .HasForeignKey(q => q.SubjectId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<QuestionBankQuestion>()
                .HasOne(q => q.Topic)
                .WithMany(t => t.Questions)
                .HasForeignKey(q => q.TopicId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<QuestionBankQuestion>()
                .HasOne(q => q.CreatedByAdmin)
                .WithMany()
                .HasForeignKey(q => q.CreatedByAdminId)
                .OnDelete(DeleteBehavior.Restrict);

            // Mirrors Question.ImportJobId's Restrict rationale in the Bulk Import section below --
            // avoids SQL Server's "multiple cascade paths" rejection.
            modelBuilder.Entity<QuestionBankQuestion>()
                .HasOne(q => q.ImportJob)
                .WithMany()
                .HasForeignKey(q => q.ImportJobId)
                .OnDelete(DeleteBehavior.Restrict);

            // Non-unique -- powers both the server-side search (LIKE/full-text on this column) and
            // the duplicate-detection check in QuestionBankImportService. A true UNIQUE index isn't
            // used here because normalization is app-level (see NormalizeForDuplicateCheck), not a
            // DB-level guarantee.
            modelBuilder.Entity<QuestionBankQuestion>()
                .HasIndex(q => q.NormalizedQuestionText);

            modelBuilder.Entity<QuestionBankQuestion>()
                .HasIndex(q => new { q.SubjectId, q.TopicId });

            modelBuilder.Entity<QuestionBankExamMapping>()
                .HasOne(m => m.QuestionBankQuestion)
                .WithMany(q => q.ExamMappings)
                .HasForeignKey(m => m.QuestionBankQuestionId)
                .OnDelete(DeleteBehavior.Cascade); // deleting a question takes its exam/year tags with it

            modelBuilder.Entity<QuestionBankExamMapping>()
                .HasOne(m => m.Exam)
                .WithMany()
                .HasForeignKey(m => m.ExamId)
                .OnDelete(DeleteBehavior.Restrict);

            // The same question can't be tagged against the same Exam+Year twice (section 6:
            // "one logical question with multiple exam/year mappings", not repeated ones).
            modelBuilder.Entity<QuestionBankExamMapping>()
                .HasIndex(m => new { m.QuestionBankQuestionId, m.ExamId, m.Year })
                .IsUnique();

            // Fast "which years/exams exist at all" lookups for the filter-dropdown endpoints.
            modelBuilder.Entity<QuestionBankExamMapping>()
                .HasIndex(m => m.ExamId);
            modelBuilder.Entity<QuestionBankExamMapping>()
                .HasIndex(m => m.Year);

            modelBuilder.Entity<QuestionBankImportJob>()
                .HasOne(j => j.CreatedByAdmin)
                .WithMany()
                .HasForeignKey(j => j.CreatedByAdminId)
                .OnDelete(DeleteBehavior.Restrict);

            // QuestionSolution / QuestionReport: both QuestionId and QuestionBankQuestionId are now
            // nullable (see QuestionModels.cs) so the same table serves either question type.
            // QuestionId keeps its original Cascade behavior (it was convention-based Cascade before
            // this feature -- deleting a legacy Question already relied on that to also remove its
            // Solutions/Reports; changing it to Restrict here would break that existing delete flow).
            // QuestionBankQuestionId gets the same Cascade for the same reason on the new entity. This
            // is safe from SQL Server's "multiple cascade paths" rejection because Question and
            // QuestionBankQuestion are two unrelated parent tables with no path between them -- that
            // restriction only bites when the SAME parent table reaches a child through more than one
            // route (see AdminTask's two Admin FKs above), which isn't the case here.
            modelBuilder.Entity<QuestionSolution>()
                .HasOne(s => s.Question)
                .WithMany(q => q.Solutions)
                .HasForeignKey(s => s.QuestionId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<QuestionSolution>()
                .HasOne(s => s.QuestionBankQuestion)
                .WithMany(q => q.Solutions)
                .HasForeignKey(s => s.QuestionBankQuestionId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<QuestionReport>()
                .HasOne(r => r.Question)
                .WithMany(q => q.Reports)
                .HasForeignKey(r => r.QuestionId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<QuestionReport>()
                .HasOne(r => r.QuestionBankQuestion)
                .WithMany(q => q.Reports)
                .HasForeignKey(r => r.QuestionBankQuestionId)
                .OnDelete(DeleteBehavior.Cascade);

            // ==========================================================================
            // Comments / Discussion threads on Question Bank questions, and per-user voting
            // ==========================================================================

            // QuestionComment: same dual-nullable-FK reuse as Solutions/Reports above, so one comment
            // thread implementation serves both the legacy Question and QuestionBankQuestion.
            modelBuilder.Entity<QuestionComment>()
                .HasOne(c => c.Question)
                .WithMany(q => q.Comments)
                .HasForeignKey(c => c.QuestionId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<QuestionComment>()
                .HasOne(c => c.QuestionBankQuestion)
                .WithMany(q => q.Comments)
                .HasForeignKey(c => c.QuestionBankQuestionId)
                .OnDelete(DeleteBehavior.Cascade);

            // CommentVote -- one row per (user, comment). Restrict on Comment (not Cascade): a
            // comment's own delete path already collects and removes its votes explicitly wherever
            // needed (see DiscussionsController.RemoveReportedComment's subtree removal), so this
            // avoids a second, implicit cascade route into the same table alongside that.
            modelBuilder.Entity<CommentVote>()
                .HasOne(v => v.Comment)
                .WithMany()
                .HasForeignKey(v => v.CommentId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<CommentVote>()
                .HasOne(v => v.User)
                .WithMany()
                .HasForeignKey(v => v.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<CommentVote>()
                .HasIndex(v => new { v.UserId, v.CommentId })
                .IsUnique();

            // QuestionVote -- Like/Dislike on the question itself (distinct from a CommentVote on one
            // reply within its discussion thread). Same dual-nullable-FK pattern; a filtered unique
            // index per FK since exactly one of QuestionId/QuestionBankQuestionId is ever set (a plain
            // unique index on both columns together wouldn't stop two votes that both leave the OTHER
            // column null from colliding as "equal").
            modelBuilder.Entity<QuestionVote>()
                .HasOne(v => v.Question)
                .WithMany(q => q.Votes)
                .HasForeignKey(v => v.QuestionId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<QuestionVote>()
                .HasOne(v => v.QuestionBankQuestion)
                .WithMany(q => q.Votes)
                .HasForeignKey(v => v.QuestionBankQuestionId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<QuestionVote>()
                .HasOne(v => v.User)
                .WithMany()
                .HasForeignKey(v => v.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<QuestionVote>()
                .HasIndex(v => new { v.UserId, v.QuestionId })
                .IsUnique()
                .HasFilter("[QuestionId] IS NOT NULL");

            modelBuilder.Entity<QuestionVote>()
                .HasIndex(v => new { v.UserId, v.QuestionBankQuestionId })
                .IsUnique()
                .HasFilter("[QuestionBankQuestionId] IS NOT NULL");

            // ==========================================================================
            // Azure Blob Storage (Documents)
            // ==========================================================================

            modelBuilder.Entity<Document>().Property(d => d.Category).HasConversion<string>().HasMaxLength(20);

            // The blob key must be unique -- two rows should never point at the same object in the
            // container.
            modelBuilder.Entity<Document>()
                .HasIndex(d => d.BlobName)
                .IsUnique();

            // Fast "list this admin's / this student's uploads" + fast "list all PYQ PDFs" style
            // admin lookups.
            modelBuilder.Entity<Document>()
                .HasIndex(d => d.Category);
            modelBuilder.Entity<Document>()
                .HasIndex(d => d.UploadedByUserId);
            modelBuilder.Entity<Document>()
                .HasIndex(d => d.UploadedByAdminId);

            // Restrict (not Cascade) on both -- same reasoning as every other dual-FK entity in this
            // file (QuestionSolution, QuestionComment, ...): deleting a User/Admin should never
            // silently delete file metadata out from under a still-referenced blob. Both FKs point at
            // different tables (User vs Admin), so this doesn't hit SQL Server's "multiple cascade
            // paths into the same table" restriction that forces Restrict elsewhere in this file.
            modelBuilder.Entity<Document>()
                .HasOne(d => d.UploadedByUser)
                .WithMany()
                .HasForeignKey(d => d.UploadedByUserId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<Document>()
                .HasOne(d => d.UploadedByAdmin)
                .WithMany()
                .HasForeignKey(d => d.UploadedByAdminId)
                .OnDelete(DeleteBehavior.Restrict);

            // ==========================================================================
            // Bookmarks -- one table, five possible targets (see Models/Bookmark.cs)
            // ==========================================================================

            modelBuilder.Entity<Bookmark>()
                .HasOne(b => b.User)
                .WithMany()
                .HasForeignKey(b => b.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            // Restrict (not Cascade) on every content-target FK below: Question and QuestionBank
            // Question both already cascade into QuestionComment (Comment), and Paper cascades into
            // Question -- so a direct Cascade here as well would give SQL Server two different
            // cascade paths converging on this same Bookmarks table (one direct, one via Comment),
            // which it refuses at schema-creation time. Same reasoning as the
            // "Restrict cascade deletes where it would create multiple cascade paths" block near the
            // top of this method. In practice this content is soft-deleted (IsActive/Status flags)
            // rather than hard-deleted, so this rarely bites -- and if it ever needs to, the
            // deleting code can remove the relevant Bookmark rows explicitly first.
            modelBuilder.Entity<Bookmark>()
                .HasOne(b => b.Question)
                .WithMany()
                .HasForeignKey(b => b.QuestionId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<Bookmark>()
                .HasOne(b => b.QuestionBankQuestion)
                .WithMany()
                .HasForeignKey(b => b.QuestionBankQuestionId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<Bookmark>()
                .HasOne(b => b.Comment)
                .WithMany()
                .HasForeignKey(b => b.CommentId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<Bookmark>()
                .HasOne(b => b.Paper)
                .WithMany()
                .HasForeignKey(b => b.PaperId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<Bookmark>()
                .HasOne(b => b.MockTest)
                .WithMany()
                .HasForeignKey(b => b.MockTestId)
                .OnDelete(DeleteBehavior.Restrict);

            // One bookmark per (user, target) -- a filtered unique index per target column since
            // exactly one of the five is ever set (same reasoning as QuestionVote's filtered indexes
            // above: a plain unique index across all columns together wouldn't stop two DIFFERENT
            // bookmarks that both leave every OTHER column null from colliding as "equal").
            modelBuilder.Entity<Bookmark>()
                .HasIndex(b => new { b.UserId, b.QuestionId })
                .IsUnique()
                .HasFilter("[QuestionId] IS NOT NULL");

            modelBuilder.Entity<Bookmark>()
                .HasIndex(b => new { b.UserId, b.QuestionBankQuestionId })
                .IsUnique()
                .HasFilter("[QuestionBankQuestionId] IS NOT NULL");

            modelBuilder.Entity<Bookmark>()
                .HasIndex(b => new { b.UserId, b.CommentId })
                .IsUnique()
                .HasFilter("[CommentId] IS NOT NULL");

            modelBuilder.Entity<Bookmark>()
                .HasIndex(b => new { b.UserId, b.PaperId })
                .IsUnique()
                .HasFilter("[PaperId] IS NOT NULL");

            modelBuilder.Entity<Bookmark>()
                .HasIndex(b => new { b.UserId, b.MockTestId })
                .IsUnique()
                .HasFilter("[MockTestId] IS NOT NULL");

            // ==========================================================================
            // "My Exams" -- UserExamPreference (see Models/UserExamPreference.cs)
            // ==========================================================================

            modelBuilder.Entity<UserExamPreference>()
                .HasOne(p => p.User)
                .WithMany()
                .HasForeignKey(p => p.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            // Restrict (not Cascade), same reasoning as every other student-facing Exam FK in this
            // file (PracticeTestTemplate.Exam, etc.): an Exam with real content attached already
            // can't be hard-deleted at all (see ExamsController.Delete's own content check), so this
            // only ever matters for the Exam-genuinely-has-nothing-else-attached case -- and even
            // then, a student's own saved preference shouldn't silently vanish as a side effect of
            // an unrelated admin action elsewhere.
            modelBuilder.Entity<UserExamPreference>()
                .HasOne(p => p.Exam)
                .WithMany()
                .HasForeignKey(p => p.ExamId)
                .OnDelete(DeleteBehavior.Restrict);

            // A student can't select the same exam twice (spec section 35).
            modelBuilder.Entity<UserExamPreference>()
                .HasIndex(p => new { p.UserId, p.ExamId })
                .IsUnique();

            // At most one Primary Exam per student (spec section 5) -- a single-column filtered
            // unique index rather than app-code-only enforcement, same pattern as Bookmark's
            // filtered indexes above: SQL Server allows any number of rows where IsPrimary = 0
            // through this filter, but only ever one per UserId where IsPrimary = 1.
            modelBuilder.Entity<UserExamPreference>()
                .HasIndex(p => p.UserId)
                .IsUnique()
                .HasFilter("[IsPrimary] = 1");
        }
    }

    // Store: pass the value through as-is -- everywhere in the codebase already writes UTC
    // (DateTime.UtcNow), and SQL Server's datetime2 has no timezone concept to convert to anyway.
    // Read: always stamp Kind = Utc on the way back out of the database, regardless of whatever
    // Unspecified/Local kind EF Core's raw ADO.NET read handed us. This is what makes
    // System.Text.Json emit the trailing "Z" the frontend needs to parse timestamps as UTC.
    public class UtcDateTimeConverter : ValueConverter<DateTime, DateTime>
    {
        public UtcDateTimeConverter() : base(
            v => v,
            v => DateTime.SpecifyKind(v, DateTimeKind.Utc))
        {
        }
    }

    public class NullableUtcDateTimeConverter : ValueConverter<DateTime?, DateTime?>
    {
        public NullableUtcDateTimeConverter() : base(
            v => v,
            v => v.HasValue ? DateTime.SpecifyKind(v.Value, DateTimeKind.Utc) : v)
        {
        }
    }
}