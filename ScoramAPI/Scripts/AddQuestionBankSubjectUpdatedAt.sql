-- ============================================================================
-- Subject Management -- adds QuestionBankSubjects.UpdatedAt
-- ============================================================================
-- Equivalent of the EF migration 20260929120000_AddQuestionBankSubjectUpdatedAt,
-- for environments where migrations are applied by hand instead of
-- `dotnet ef database update`. Safe to run more than once.
--
-- WHAT IT DOES: adds ONE nullable datetime2 column. No existing row is changed
-- or rewritten, nothing is dropped, and NULL simply means "never edited from
-- Subject Management" (the admin list falls back to the created date).
--
-- ORDER OF DEPLOYMENT: run this (or the EF migration) BEFORE the new API build
-- goes live -- the API model now includes UpdatedAt, so every query that touches
-- QuestionBankSubjects would fail on a database that doesn't have the column yet.
--
-- ROLLBACK (only if the new API build is also rolled back):
--   ALTER TABLE dbo.QuestionBankSubjects DROP COLUMN UpdatedAt;
--   DELETE FROM dbo.__EFMigrationsHistory WHERE MigrationId = N'20260929120000_AddQuestionBankSubjectUpdatedAt';
-- ============================================================================

BEGIN TRANSACTION;

IF COL_LENGTH(N'dbo.QuestionBankSubjects', N'UpdatedAt') IS NULL
BEGIN
    ALTER TABLE dbo.QuestionBankSubjects ADD UpdatedAt datetime2 NULL;
END

-- Record it in EF's history table so a later `dotnet ef database update` doesn't try to add it again.
IF NOT EXISTS (SELECT 1 FROM dbo.__EFMigrationsHistory WHERE MigrationId = N'20260929120000_AddQuestionBankSubjectUpdatedAt')
BEGIN
    INSERT INTO dbo.__EFMigrationsHistory (MigrationId, ProductVersion)
    VALUES (N'20260929120000_AddQuestionBankSubjectUpdatedAt', N'8.0.8');
END

COMMIT TRANSACTION;

-- Verify:
-- SELECT TOP 5 Id, Name, IsActive, CreatedAt, UpdatedAt FROM dbo.QuestionBankSubjects ORDER BY Name;
