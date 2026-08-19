-- ============================================================================
-- Shift removal — data migration (run BEFORE `dotnet ef database update` for
-- the migration that drops Papers.Shift)
-- ============================================================================
-- Papers used to be unique by Exam+Year+Shift+Language+PaperCode. Now that
-- Shift is gone, any papers that only differed by Shift (same Exam+Year+
-- Language+PaperCode) would look like duplicates. This script finds them and
-- merges each group into one surviving paper, moving every Question across
-- and renumbering QuestionNumber so nothing collides.
--
-- ⚠ THIS CHANGES REAL DATA. Two questions that were "Shift 1, Q1" and
-- "Shift 2, Q1" become two different question numbers inside one merged
-- paper. If that's not what you want for a specific group, handle it by hand
-- instead of running Part 2 for that group (e.g. rename one paper's
-- PaperCode first so it no longer collides, then it's left alone).
--
-- WORKFLOW:
--   1. Run PART 1 only. Review the report. If it's empty, you have nothing
--      to merge -- skip straight to the EF migration.
--   2. If it looks right, run PART 2 inside a transaction (included below)
--      and verify the results before COMMIT.
--   3. Only then run `dotnet ef database update` to actually drop the
--      Shift column.
-- ============================================================================

USE ScoramDB;
GO

-- ============================================================================
-- PART 1 — REPORT ONLY (read-only, safe to run any time)
-- ============================================================================
SELECT
    ExamId,
    Year,
    Language,
    PaperCode,
    COUNT(*)                         AS PaperCount,
    STRING_AGG(CAST(Id AS NVARCHAR(36)) + ' (Shift: ' + ISNULL(Shift, 'none') + ', ' + CAST([Status] AS NVARCHAR(20)) + ')', ' | ')
                                      AS PapersInGroup
FROM dbo.Papers
GROUP BY ExamId, Year, Language, PaperCode
HAVING COUNT(*) > 1
ORDER BY ExamId, Year, Language, PaperCode;
GO

-- If the above returns zero rows, there's nothing to merge -- go straight to
-- the EF migration. If it returns rows, review each group before Part 2.

-- ============================================================================
-- PART 2 — THE ACTUAL MERGE (run inside a transaction; review before COMMIT)
-- ============================================================================
BEGIN TRANSACTION;

-- One row per duplicate group, with the survivor picked as the earliest-
-- created paper in the group (arbitrary but consistent choice).
;WITH Groups AS (
    SELECT
        Id, ExamId, Year, Language, PaperCode, CreatedAt,
        FIRST_VALUE(Id) OVER (
            PARTITION BY ExamId, Year, Language, PaperCode
            ORDER BY CreatedAt ASC, Id ASC
        ) AS SurvivorId,
        COUNT(*) OVER (PARTITION BY ExamId, Year, Language, PaperCode) AS GroupSize
    FROM dbo.Papers
),
Duplicates AS (
    SELECT Id AS DuplicatePaperId, SurvivorId
    FROM Groups
    WHERE GroupSize > 1 AND Id <> SurvivorId
),
-- Every question being moved, renumbered to continue after the survivor's
-- current highest QuestionNumber, in original-QuestionNumber order so the
-- relative sequence within each merged-in paper is preserved.
Renumbered AS (
    SELECT
        q.Id AS QuestionId,
        d.SurvivorId,
        (
            SELECT ISNULL(MAX(sq.QuestionNumber), 0)
            FROM dbo.Questions sq
            WHERE sq.PaperId = d.SurvivorId
        )
        + ROW_NUMBER() OVER (PARTITION BY d.SurvivorId ORDER BY q.QuestionNumber) AS NewQuestionNumber
    FROM dbo.Questions q
    INNER JOIN Duplicates d ON q.PaperId = d.DuplicatePaperId
)
UPDATE q
SET q.PaperId = r.SurvivorId,
    q.QuestionNumber = r.NewQuestionNumber
FROM dbo.Questions q
INNER JOIN Renumbered r ON q.Id = r.QuestionId;

-- Every duplicate paper should now have zero Questions left -- delete them.
;WITH Groups AS (
    SELECT
        Id, ExamId, Year, Language, PaperCode,
        FIRST_VALUE(Id) OVER (
            PARTITION BY ExamId, Year, Language, PaperCode
            ORDER BY CreatedAt ASC, Id ASC
        ) AS SurvivorId,
        COUNT(*) OVER (PARTITION BY ExamId, Year, Language, PaperCode) AS GroupSize
    FROM dbo.Papers
)
DELETE p
FROM dbo.Papers p
INNER JOIN Groups g ON p.Id = g.Id
WHERE g.GroupSize > 1 AND g.Id <> g.SurvivorId;

-- Sanity check before committing: this should now return zero rows.
SELECT ExamId, Year, Language, PaperCode, COUNT(*) AS StillDuplicated
FROM dbo.Papers
GROUP BY ExamId, Year, Language, PaperCode
HAVING COUNT(*) > 1;

-- Review the result above, then either:
COMMIT TRANSACTION;
-- or, if anything looks wrong:
-- ROLLBACK TRANSACTION;
GO
