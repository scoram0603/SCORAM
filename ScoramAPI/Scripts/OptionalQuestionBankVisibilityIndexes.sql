-- ============================================================================
-- OPTIONAL performance indexes (audit SQL-01 / SQL-04) -- NOT an EF migration, NOT applied
-- automatically, and NOT run by anyone yet. CANDIDATES to be measured, not a known win.
-- ============================================================================
-- Context: the student-facing Question Bank visibility rule
--   IsActive = 1 AND IsTestOnly = 0 AND (no mirror source OR a mirror source whose Paper is Published)
-- (QuestionBankController.VisibleQuestions) has no index support for IsTestOnly, and
-- GET /api/exams (API-07) now runs one GROUP BY over QuestionBankExamMappings.
--
-- IMPORTANT -- written WITHOUT access to a database or a query plan. Every index below is a
-- hypothesis. The EXISTING indexes already cover a lot (see "Existing" under each), so apply each one
-- only if a real before/after comparison on a staging copy shows it helps.
--
-- Safe properties:
--   * Each statement adds ONE non-clustered index. No column, row, constraint or existing index is
--     touched or dropped. Query results do not change; only the plans the optimiser can choose.
--   * Idempotent: each index is created only if it does not already exist.
--   * The EF model snapshot is NOT changed, so `dotnet ef migrations` behaviour is unaffected. If you
--     keep an index permanently, mirror it in ScoramDbContext + a migration later (otherwise a future
--     scaffolded migration will not know about it -- harmless, but it will not be recreated from EF).
--
-- BEFORE / AFTER procedure (do this on a staging COPY of production data, at a quiet time):
--   1. SET STATISTICS IO, TIME ON; and capture the ACTUAL execution plan for each query below.
--      Easiest source of the exact SQL: enable EF's sensitive logging on staging, or use Query Store /
--      sys.dm_exec_query_stats to find the statements these endpoints run:
--        GET /api/exams                          (the new grouped bank-count query)
--        GET /api/question-bank/search           (count + page query, no keyword and with filters)
--        GET /api/question-bank/subjects|topics|exams|years
--   2. Record logical reads + CPU/elapsed for each. Run each 3x (warm cache) and use the median.
--   3. Apply ONE index, repeat step 2, compare. Keep it only if reads/CPU drop materially and the
--      plan actually uses it. Indexes add write cost to every QuestionBank import / admin edit.
--   4. ONLINE = ON only if your edition supports it (Enterprise / Azure SQL); otherwise leave it out
--      (these scripts do not use it).
--
-- Read-only selectivity check (run first; tells you whether Candidate 3 is worth it at all):
--   SELECT COUNT(*) AS Total,
--          SUM(CASE WHEN IsActive = 1 AND IsTestOnly = 0 THEN 1 ELSE 0 END) AS VisibleRows,
--          SUM(CASE WHEN IsTestOnly = 1 THEN 1 ELSE 0 END) AS TestOnlyRows
--   FROM dbo.QuestionBankQuestions;
--   -- If VisibleRows is ~ Total, a filtered index is nearly the whole table: skip Candidate 3.
-- ============================================================================

SET QUOTED_IDENTIFIER ON;   -- required for filtered indexes
GO

-- ----------------------------------------------------------------------------
-- Candidate 1 (highest confidence) -- serves the API-07 GROUP BY in GET /api/exams.
--   Query shape: QuestionBankExamMappings WHERE ExamId IN (...) joined to QuestionBankQuestions,
--   grouped by ExamId, COUNT(DISTINCT QuestionBankQuestionId).
--   Existing: UNIQUE (QuestionBankQuestionId, ExamId, Year) leads with the question id; a separate
--   single-column ExamId index exists but does not carry QuestionBankQuestionId, so the
--   COUNT(DISTINCT) needs a lookup per row. (ExamId, QuestionBankQuestionId) is seekable by exam and
--   delivers the question ids already ordered for the DISTINCT.
--   Note: once this exists the single-column ExamId index is a strict prefix of it (redundant for
--   most plans). Do NOT drop it as part of this script; decide after measuring.
-- ROLLBACK: DROP INDEX IX_QuestionBankExamMappings_ExamId_QuestionBankQuestionId ON dbo.QuestionBankExamMappings;
-- ----------------------------------------------------------------------------
IF NOT EXISTS (
    SELECT 1 FROM sys.indexes
    WHERE name = N'IX_QuestionBankExamMappings_ExamId_QuestionBankQuestionId'
      AND object_id = OBJECT_ID(N'dbo.QuestionBankExamMappings'))
BEGIN
    CREATE NONCLUSTERED INDEX IX_QuestionBankExamMappings_ExamId_QuestionBankQuestionId
        ON dbo.QuestionBankExamMappings (ExamId, QuestionBankQuestionId);
    PRINT 'Created IX_QuestionBankExamMappings_ExamId_QuestionBankQuestionId';
END
ELSE
    PRINT 'IX_QuestionBankExamMappings_ExamId_QuestionBankQuestionId already exists; nothing to do.';
GO

-- ----------------------------------------------------------------------------
-- Candidate 2 (medium confidence) -- the "is this bank question a mirror, and is its paper
-- Published?" correlated EXISTS checks (visibility predicate + API-07's NOT EXISTS).
--   Existing: single-column index on Questions(MirroredToQuestionBankQuestionId). A seek finds the
--   source rows, but reading PaperId (to join Papers.Status) needs a key lookup into the clustered
--   index for each one. INCLUDE (PaperId) makes the seek covering for these checks.
--   Cost: Questions is a large, write-heavy table (paper uploads); this adds one more index there.
--   Existing index is NOT dropped here -- once this exists it is redundant; decide after measuring.
-- ROLLBACK: DROP INDEX IX_Questions_MirroredToQbq_IncPaperId ON dbo.Questions;
-- ----------------------------------------------------------------------------
IF NOT EXISTS (
    SELECT 1 FROM sys.indexes
    WHERE name = N'IX_Questions_MirroredToQbq_IncPaperId'
      AND object_id = OBJECT_ID(N'dbo.Questions'))
BEGIN
    CREATE NONCLUSTERED INDEX IX_Questions_MirroredToQbq_IncPaperId
        ON dbo.Questions (MirroredToQuestionBankQuestionId)
        INCLUDE (PaperId);
    PRINT 'Created IX_Questions_MirroredToQbq_IncPaperId';
END
ELSE
    PRINT 'IX_Questions_MirroredToQbq_IncPaperId already exists; nothing to do.';
GO

-- ----------------------------------------------------------------------------
-- Candidate 3 (LOWEST confidence -- check the selectivity query above first) -- the search
-- count/page query: WHERE IsActive = 1 AND IsTestOnly = 0 ... ORDER BY CreatedAt DESC.
--   Existing: (IsActive, CreatedAt) cannot filter on IsTestOnly, so test-only rows are read and
--   discarded. A FILTERED index on exactly the visible rows, ordered by CreatedAt DESC, lets the
--   optimiser skip them. EF emits the filter as literal constants, so the optimiser can match it;
--   a plan that does not show this index is the sign it is not helping -> don't keep it.
--   Worthless if test-only/inactive rows are a tiny share of the table (see selectivity check).
-- ROLLBACK: DROP INDEX IX_QuestionBankQuestions_Visible_CreatedAt ON dbo.QuestionBankQuestions;
-- ----------------------------------------------------------------------------
IF NOT EXISTS (
    SELECT 1 FROM sys.indexes
    WHERE name = N'IX_QuestionBankQuestions_Visible_CreatedAt'
      AND object_id = OBJECT_ID(N'dbo.QuestionBankQuestions'))
BEGIN
    CREATE NONCLUSTERED INDEX IX_QuestionBankQuestions_Visible_CreatedAt
        ON dbo.QuestionBankQuestions (CreatedAt DESC)
        INCLUDE (SubjectId, TopicId, Language)
        WHERE IsActive = 1 AND IsTestOnly = 0;
    PRINT 'Created IX_QuestionBankQuestions_Visible_CreatedAt';
END
ELSE
    PRINT 'IX_QuestionBankQuestions_Visible_CreatedAt already exists; nothing to do.';
GO
