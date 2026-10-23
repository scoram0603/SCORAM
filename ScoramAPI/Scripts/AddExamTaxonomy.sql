-- ============================================================================
-- Exam-wise Subject & Topic Management -- creates ExamSubjects, ExamTopics,
-- QuestionBankExamClassifications
-- ============================================================================
-- Equivalent of the EF migration 20261022120000_AddExamTaxonomy, for environments
-- where migrations are applied by hand. Safe to run more than once.
--
-- WHAT IT DOES: creates THREE NEW tables (+ indexes). No existing table or column
-- is altered; no existing row is read, moved or rewritten. Existing exams behave
-- exactly as before until an admin runs "Initialise" for that exam in the admin
-- panel (a previewed, confirmed action).
--
-- ORDER OF DEPLOYMENT: run this BEFORE the new API build goes live (the new
-- student Question Bank endpoints query these tables).
--
-- ROLLBACK (only together with rolling back the API build; this DELETES the
-- exam-specific taxonomy, never any question/paper/test):
--   DROP TABLE dbo.QuestionBankExamClassifications;
--   DROP TABLE dbo.ExamTopics;
--   DROP TABLE dbo.ExamSubjects;
--   DELETE FROM dbo.__EFMigrationsHistory WHERE MigrationId = N'20261022120000_AddExamTaxonomy';
-- ============================================================================

BEGIN TRANSACTION;

IF OBJECT_ID(N'dbo.ExamSubjects', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.ExamSubjects (
        Id               uniqueidentifier NOT NULL CONSTRAINT PK_ExamSubjects PRIMARY KEY,
        ExamId           uniqueidentifier NOT NULL,
        BaseSubjectId    uniqueidentifier NULL,
        Name             nvarchar(100)    NOT NULL,
        IsActive         bit              NOT NULL,
        CreatedByAdminId uniqueidentifier NOT NULL,
        CreatedAt        datetime2        NOT NULL,
        UpdatedAt        datetime2        NULL,
        CONSTRAINT FK_ExamSubjects_Admins_CreatedByAdminId FOREIGN KEY (CreatedByAdminId) REFERENCES dbo.Admins (Id),
        CONSTRAINT FK_ExamSubjects_Exams_ExamId FOREIGN KEY (ExamId) REFERENCES dbo.Exams (Id),
        CONSTRAINT FK_ExamSubjects_QuestionBankSubjects_BaseSubjectId FOREIGN KEY (BaseSubjectId) REFERENCES dbo.QuestionBankSubjects (Id)
    );
    CREATE UNIQUE INDEX UX_ExamSubjects_Exam_Name_Active ON dbo.ExamSubjects (ExamId, Name) WHERE [IsActive] = 1;
    CREATE UNIQUE INDEX UX_ExamSubjects_Exam_Base ON dbo.ExamSubjects (ExamId, BaseSubjectId) WHERE [BaseSubjectId] IS NOT NULL;
    CREATE INDEX IX_ExamSubjects_BaseSubjectId ON dbo.ExamSubjects (BaseSubjectId);
    CREATE INDEX IX_ExamSubjects_CreatedByAdminId ON dbo.ExamSubjects (CreatedByAdminId);
END

IF OBJECT_ID(N'dbo.ExamTopics', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.ExamTopics (
        Id               uniqueidentifier NOT NULL CONSTRAINT PK_ExamTopics PRIMARY KEY,
        ExamSubjectId    uniqueidentifier NOT NULL,
        BaseTopicId      uniqueidentifier NULL,
        Name             nvarchar(150)    NOT NULL,
        IsActive         bit              NOT NULL,
        CreatedByAdminId uniqueidentifier NOT NULL,
        CreatedAt        datetime2        NOT NULL,
        UpdatedAt        datetime2        NULL,
        CONSTRAINT FK_ExamTopics_Admins_CreatedByAdminId FOREIGN KEY (CreatedByAdminId) REFERENCES dbo.Admins (Id),
        CONSTRAINT FK_ExamTopics_ExamSubjects_ExamSubjectId FOREIGN KEY (ExamSubjectId) REFERENCES dbo.ExamSubjects (Id),
        CONSTRAINT FK_ExamTopics_QuestionBankTopics_BaseTopicId FOREIGN KEY (BaseTopicId) REFERENCES dbo.QuestionBankTopics (Id)
    );
    CREATE UNIQUE INDEX UX_ExamTopics_Subject_Name_Active ON dbo.ExamTopics (ExamSubjectId, Name) WHERE [IsActive] = 1;
    CREATE INDEX IX_ExamTopics_BaseTopicId ON dbo.ExamTopics (BaseTopicId);
    CREATE INDEX IX_ExamTopics_CreatedByAdminId ON dbo.ExamTopics (CreatedByAdminId);
END

IF OBJECT_ID(N'dbo.QuestionBankExamClassifications', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.QuestionBankExamClassifications (
        Id                     uniqueidentifier NOT NULL CONSTRAINT PK_QuestionBankExamClassifications PRIMARY KEY,
        QuestionBankQuestionId uniqueidentifier NOT NULL,
        ExamId                 uniqueidentifier NOT NULL,
        ExamSubjectId          uniqueidentifier NULL,
        ExamTopicId            uniqueidentifier NULL,
        CreatedAt              datetime2        NOT NULL,
        UpdatedAt              datetime2        NULL,
        UpdatedByAdminId       uniqueidentifier NULL,
        CONSTRAINT FK_QuestionBankExamClassifications_Exams_ExamId FOREIGN KEY (ExamId) REFERENCES dbo.Exams (Id),
        CONSTRAINT FK_QuestionBankExamClassifications_ExamSubjects_ExamSubjectId FOREIGN KEY (ExamSubjectId) REFERENCES dbo.ExamSubjects (Id),
        CONSTRAINT FK_QuestionBankExamClassifications_ExamTopics_ExamTopicId FOREIGN KEY (ExamTopicId) REFERENCES dbo.ExamTopics (Id),
        CONSTRAINT FK_QuestionBankExamClassifications_QuestionBankQuestions_QuestionBankQuestionId FOREIGN KEY (QuestionBankQuestionId) REFERENCES dbo.QuestionBankQuestions (Id) ON DELETE CASCADE
    );
    CREATE UNIQUE INDEX UX_QBExamClassifications_Question_Exam ON dbo.QuestionBankExamClassifications (QuestionBankQuestionId, ExamId);
    CREATE INDEX IX_QuestionBankExamClassifications_ExamId_ExamSubjectId ON dbo.QuestionBankExamClassifications (ExamId, ExamSubjectId);
    CREATE INDEX IX_QuestionBankExamClassifications_ExamSubjectId ON dbo.QuestionBankExamClassifications (ExamSubjectId);
    CREATE INDEX IX_QuestionBankExamClassifications_ExamTopicId ON dbo.QuestionBankExamClassifications (ExamTopicId);
END

IF NOT EXISTS (SELECT 1 FROM dbo.__EFMigrationsHistory WHERE MigrationId = N'20261022120000_AddExamTaxonomy')
BEGIN
    INSERT INTO dbo.__EFMigrationsHistory (MigrationId, ProductVersion)
    VALUES (N'20261022120000_AddExamTaxonomy', N'8.0.8');
END

COMMIT TRANSACTION;

-- Verify:
-- SELECT COUNT(*) FROM dbo.ExamSubjects; SELECT COUNT(*) FROM dbo.ExamTopics; SELECT COUNT(*) FROM dbo.QuestionBankExamClassifications;
