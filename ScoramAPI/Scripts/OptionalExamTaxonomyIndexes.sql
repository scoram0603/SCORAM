-- ============================================================================
-- OPTIONAL index candidates for the exam taxonomy lookups (audit P2, round 5) -- NOT an EF migration,
-- NOT applied automatically, and NOT run by anyone yet. CANDIDATES to be measured, not a known win.
-- ============================================================================
-- Context: ExamTaxonomyService reads ExamSubjects by ExamId (IsInitializedAsync, LoadTaxonomyAsync) and,
-- since round 5, joins ExamSubjects / ExamTopics from the requested ids to QuestionBankQuestions in
-- FilterByExamSubjects / FilterByExamTopics. The only existing indexes that lead with those columns are
-- FILTERED unique indexes (UX_ExamSubjects_Exam_Name_Active WHERE IsActive = 1,
-- UX_ExamSubjects_Exam_Base WHERE BaseSubjectId IS NOT NULL, UX_ExamTopics_Subject_Name_Active WHERE
-- IsActive = 1). SQL Server cannot use a filtered index for `WHERE ExamId = @x` unless it can prove the
-- query implies the filter, so those lookups currently scan the (small) tables.
--
-- IMPORTANT -- written WITHOUT access to a database or a query plan. Both tables are small (tens to a
-- few hundred rows per exam), so the expected gain is modest; apply only if a before/after comparison on
-- a staging copy shows a difference.
--
-- Safe properties:
--   * Each statement adds ONE non-clustered index. No column, row, constraint or existing index is touched
--     or dropped. Query results do not change; only the plans the optimiser can choose.
--   * Idempotent: each index is created only if it does not already exist.
--   * The EF model snapshot is NOT changed. If you keep an index permanently, mirror it in ScoramDbContext
--     + a migration later (a future scaffolded migration would not know about it; harmless).
--
-- BEFORE / AFTER procedure (staging COPY of production data, at a quiet time):
--   1. SET STATISTICS IO, TIME ON; capture the ACTUAL plan of the statements these endpoints run
--      (Data/SlowQueryInterceptor.cs logs slow commands; or Query Store / sys.dm_exec_query_stats):
--        GET /api/question-bank/search?examSubjectIds=...   and   ...?examTopicIds=...
--        GET /api/question-bank/subjects?examId=...         and   /topics?examId=...
--   2. Record logical reads + CPU/elapsed (3 runs, median, warm cache).
--   3. Apply ONE index, repeat step 2, compare. Keep it only if the plan really uses it and reads/CPU drop.
--      Every extra index adds a little write cost to taxonomy edits (rare).
--
-- Read-only size check (run first -- if both tables are tiny, skip this script entirely):
--   SELECT (SELECT COUNT(*) FROM dbo.ExamSubjects) AS ExamSubjectRows,
--          (SELECT COUNT(*) FROM dbo.ExamTopics)   AS ExamTopicRows;
-- ============================================================================

SET QUOTED_IDENTIFIER ON;
GO

-- ----------------------------------------------------------------------------
-- Candidate 1 -- ExamSubjects by ExamId.
--   Serves: WHERE ExamId = @x (IsInitializedAsync, LoadTaxonomyAsync) and carries BaseSubjectId.
-- ROLLBACK: DROP INDEX IX_ExamSubjects_ExamId_IncBaseSubjectId ON dbo.ExamSubjects;
-- ----------------------------------------------------------------------------
IF NOT EXISTS (
    SELECT 1 FROM sys.indexes
    WHERE name = N'IX_ExamSubjects_ExamId_IncBaseSubjectId'
      AND object_id = OBJECT_ID(N'dbo.ExamSubjects'))
BEGIN
    CREATE NONCLUSTERED INDEX IX_ExamSubjects_ExamId_IncBaseSubjectId
        ON dbo.ExamSubjects (ExamId) INCLUDE (BaseSubjectId);
    PRINT 'Created IX_ExamSubjects_ExamId_IncBaseSubjectId';
END
ELSE
    PRINT 'IX_ExamSubjects_ExamId_IncBaseSubjectId already exists; nothing to do.';
GO

-- ----------------------------------------------------------------------------
-- Candidate 2 -- ExamTopics by ExamSubjectId.
--   Serves: the join ExamTopics -> ExamSubjects in FilterByExamTopics and LoadTaxonomyAsync
--   (WHERE ExamSubjectId IN (...)); carries BaseTopicId.
-- ROLLBACK: DROP INDEX IX_ExamTopics_ExamSubjectId_IncBaseTopicId ON dbo.ExamTopics;
-- ----------------------------------------------------------------------------
IF NOT EXISTS (
    SELECT 1 FROM sys.indexes
    WHERE name = N'IX_ExamTopics_ExamSubjectId_IncBaseTopicId'
      AND object_id = OBJECT_ID(N'dbo.ExamTopics'))
BEGIN
    CREATE NONCLUSTERED INDEX IX_ExamTopics_ExamSubjectId_IncBaseTopicId
        ON dbo.ExamTopics (ExamSubjectId) INCLUDE (BaseTopicId);
    PRINT 'Created IX_ExamTopics_ExamSubjectId_IncBaseTopicId';
END
ELSE
    PRINT 'IX_ExamTopics_ExamSubjectId_IncBaseTopicId already exists; nothing to do.';
GO
