-- ============================================================================
-- OPTIONAL: SQL Server Full-Text Search setup for the fallback search paths on
-- both Questions (the PYQ/paper flow) and QuestionBankQuestions (the standalone
-- Question Bank -- see QuestionBankController.Search).
-- ============================================================================
-- Questions' fallback only kicks in when Meilisearch itself is unreachable
-- (see Services/FallbackSearchService.cs) -- Meilisearch remains the primary
-- search engine there for normal operation. QuestionBankQuestions has no
-- Meilisearch tier at all (a smaller, deliberate scope decision -- see that
-- controller's own comment), so for it this Full-Text index IS the primary
-- upgrade over LIKE, not just a during-an-outage fallback.
--
-- You do NOT need to run this script for the app to work: without it, both
-- endpoints automatically use a plain LIKE '%term%' search instead, which is
-- slower and unranked but always available.
--
-- What running this buys you: word-form-aware matching (e.g. "running" also
-- matches "run") and relevance ranking. Safe to run multiple times -- every
-- step is guarded with IF NOT EXISTS.
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

-- Same as above, for the Question Bank's own search (QuestionBankController.Search / section
-- 15-16), which had no Full-Text or Meilisearch tier at all before this -- just a plain LIKE
-- '%term%' scan. Only QuestionText goes in this one: unlike Questions.Subject/Topic (plain text
-- columns), QuestionBankQuestion.SubjectId/TopicId are FK lookups into separate
-- QuestionBankSubjects/QuestionBankTopics tables, not free text to index here.
-- KEY INDEX needs QuestionBankQuestions' PK index name -- PK_QuestionBankQuestions by EF Core's
-- default convention; look it up the same way as noted above (substituting the table name) if yours
-- differs.
IF NOT EXISTS (SELECT 1 FROM sys.fulltext_indexes WHERE object_id = OBJECT_ID('dbo.QuestionBankQuestions'))
BEGIN
    CREATE FULLTEXT INDEX ON dbo.QuestionBankQuestions
    (
        QuestionText LANGUAGE 1033
    )
    KEY INDEX PK_QuestionBankQuestions
    ON ScoramFullTextCatalog
    WITH CHANGE_TRACKING AUTO;

    PRINT 'Created full-text index on dbo.QuestionBankQuestions (QuestionText).';
END
ELSE
BEGIN
    PRINT 'Full-text index on dbo.QuestionBankQuestions already exists -- nothing to do.';
END
GO
