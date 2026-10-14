-- ============================================================================
-- OPTIONAL performance index -- NOT an EF migration, NOT applied automatically.
-- ============================================================================
-- Purpose: speeds up GET /api/test-attempts/mine (the "My attempts" list), which filters by
--          UserId and sorts by StartedAt DESC (optionally also filtering on Status).
--          The existing indexes on StudentTestResults all lead with (UserId, <test id>, Status),
--          so none of them can serve an ORDER BY StartedAt directly.
--
-- Safe properties:
--   * Adds ONE non-clustered index. No column, row, constraint or existing index is touched.
--   * Query results do not change; only the plan the optimiser can choose.
--   * Idempotent: does nothing if the index already exists.
--   * The EF model snapshot is NOT changed, so `dotnet ef migrations` behaviour is unaffected.
--
-- Before running in production:
--   1. Run on a staging copy first and compare the actual execution plan of the /mine query.
--   2. Run at a quiet time. Use ONLINE = ON only if your SQL Server edition supports it
--      (Enterprise / Azure SQL); otherwise remove that option.
--   3. Index maintenance adds a small write cost to every attempt start/submit. If staging shows no
--      measurable gain, do not apply it.
--
-- ROLLBACK:  DROP INDEX IX_StudentTestResults_UserId_StartedAt ON dbo.StudentTestResults;
-- ============================================================================

IF NOT EXISTS (
    SELECT 1 FROM sys.indexes
    WHERE name = N'IX_StudentTestResults_UserId_StartedAt'
      AND object_id = OBJECT_ID(N'dbo.StudentTestResults'))
BEGIN
    CREATE NONCLUSTERED INDEX IX_StudentTestResults_UserId_StartedAt
        ON dbo.StudentTestResults (UserId, StartedAt DESC)
        INCLUDE (Status);
    PRINT 'Created IX_StudentTestResults_UserId_StartedAt';
END
ELSE
    PRINT 'IX_StudentTestResults_UserId_StartedAt already exists; nothing to do.';
GO
