-- Optional performance indexes for the Admin Analytics layer. Idempotent: safe to run repeatedly.
-- Apply during a quiet period; each CREATE INDEX is ONLINE-capable on Enterprise/Azure SQL (ONLINE = ON is
-- ignored with an error on Standard, so it is left off). Nothing here changes data.
-- ROLLBACK: see the DROP block at the bottom.
-- NOTE: names of tables/columns below follow the EF conventions of this project; if a table is missing the
-- IF OBJECT_ID guard skips it, so the script never fails a deployment.

IF OBJECT_ID(N'dbo.Users') IS NOT NULL AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Users_CreatedAt_Analytics' AND object_id = OBJECT_ID(N'dbo.Users'))
    CREATE INDEX IX_Users_CreatedAt_Analytics ON dbo.Users (CreatedAt);

IF OBJECT_ID(N'dbo.Users') IS NOT NULL AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Users_LastActiveAt_Analytics' AND object_id = OBJECT_ID(N'dbo.Users'))
    CREATE INDEX IX_Users_LastActiveAt_Analytics ON dbo.Users (LastActiveAt);

IF OBJECT_ID(N'dbo.Papers') IS NOT NULL AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Papers_CreatedAt_Analytics' AND object_id = OBJECT_ID(N'dbo.Papers'))
    CREATE INDEX IX_Papers_CreatedAt_Analytics ON dbo.Papers (CreatedAt) INCLUDE (Status);

IF OBJECT_ID(N'dbo.AuditLogs') IS NOT NULL AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_AuditLogs_Action_CreatedAt_Analytics' AND object_id = OBJECT_ID(N'dbo.AuditLogs'))
    CREATE INDEX IX_AuditLogs_Action_CreatedAt_Analytics ON dbo.AuditLogs (Action, CreatedAt);

IF OBJECT_ID(N'dbo.UserFeedbacks') IS NOT NULL AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_UserFeedbacks_Status_CreatedAt_Analytics' AND object_id = OBJECT_ID(N'dbo.UserFeedbacks'))
    CREATE INDEX IX_UserFeedbacks_Status_CreatedAt_Analytics ON dbo.UserFeedbacks (Status, CreatedAt);
GO

/* ROLLBACK
DROP INDEX IF EXISTS IX_Users_CreatedAt_Analytics ON dbo.Users;
DROP INDEX IF EXISTS IX_Users_LastActiveAt_Analytics ON dbo.Users;
DROP INDEX IF EXISTS IX_Papers_CreatedAt_Analytics ON dbo.Papers;
DROP INDEX IF EXISTS IX_AuditLogs_Action_CreatedAt_Analytics ON dbo.AuditLogs;
DROP INDEX IF EXISTS IX_UserFeedbacks_Status_CreatedAt_Analytics ON dbo.UserFeedbacks;
*/
