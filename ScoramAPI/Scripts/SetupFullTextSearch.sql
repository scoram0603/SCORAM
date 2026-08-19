-- ============================================================================
-- OPTIONAL: SQL Server Full-Text Search setup for the fallback search path.
-- ============================================================================
-- The app only reaches this fallback when Meilisearch itself is unreachable
-- (see Services/FallbackSearchService.cs) -- Meilisearch remains the primary
-- search engine for normal operation. You do NOT need to run this script for
-- the app to work: without it, the fallback automatically uses a plain LIKE
-- '%term%' search instead, which is slower and unranked but always available.
--
-- What running this buys you: word-form-aware matching (e.g. "running" also
-- matches "run") and relevance ranking, on the rare occasions Meilisearch is
-- down. Safe to run multiple times -- every step is guarded with IF NOT EXISTS.
--
-- Requires the SQL Server Full-Text Search feature to be installed. Most
-- SQL Server / SQL Server Express installations include it, but it's an
-- optional component during setup -- if it's missing, this script exits
-- cleanly and the app keeps using LIKE search, no harm done either way.
-- ============================================================================

USE ScoramDB;
GO

IF (SELECT SERVERPROPERTY('IsFullTextInstalled')) = 0
BEGIN
    PRINT 'SQL Server Full-Text Search is not installed on this instance. Skipping setup -- the app will keep using its LIKE-based fallback, which needs no setup.';
    RETURN;
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.fulltext_catalogs WHERE name = 'ScoramFullTextCatalog')
BEGIN
    CREATE FULLTEXT CATALOG ScoramFullTextCatalog AS DEFAULT;
    PRINT 'Created full-text catalog: ScoramFullTextCatalog';
END
GO

-- CREATE FULLTEXT INDEX needs a unique, single-column, non-nullable index to key off of --
-- the Questions primary key (named PK_Questions by EF Core's default convention) qualifies.
-- If your PK index has a different name, look it up first with:
--   SELECT name FROM sys.indexes WHERE object_id = OBJECT_ID('dbo.Questions') AND is_primary_key = 1;
-- and substitute it below.
IF NOT EXISTS (SELECT 1 FROM sys.fulltext_indexes WHERE object_id = OBJECT_ID('dbo.Questions'))
BEGIN
    CREATE FULLTEXT INDEX ON dbo.Questions
    (
        QuestionText LANGUAGE 1033,  -- 1033 = English. QuestionText is the main free-text field.
        Subject      LANGUAGE 1033,
        Topic        LANGUAGE 1033
    )
    KEY INDEX PK_Questions
    ON ScoramFullTextCatalog
    WITH CHANGE_TRACKING AUTO;  -- keeps the index in sync automatically as questions are added/edited

    PRINT 'Created full-text index on dbo.Questions (QuestionText, Subject, Topic).';
END
ELSE
BEGIN
    PRINT 'Full-text index on dbo.Questions already exists -- nothing to do.';
END
GO
