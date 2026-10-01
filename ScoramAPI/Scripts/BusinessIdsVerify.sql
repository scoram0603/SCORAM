-- BusinessIdsVerify.sql -- READ-ONLY checks to run after the AddBusinessIds migration has been applied
-- and the app has started once (which runs the one-time backfill). Every result set below should be
-- EMPTY (or, where noted, show the expected shape). Changes nothing.

-- 1. The backfill finished (expect exactly 1 row).
SELECT CounterKey, LastNumber FROM BusinessIdCounters WHERE CounterKey = '__BACKFILL_COMPLETE__';

-- 2. No record is left without a Business ID (expect 0 rows in each).
SELECT 'Exams' AS T, COUNT(*) AS Missing FROM Exams WHERE BusinessId IS NULL
UNION ALL SELECT 'QuestionBankSubjects', COUNT(*) FROM QuestionBankSubjects WHERE BusinessId IS NULL
UNION ALL SELECT 'PracticeTestTemplates', COUNT(*) FROM PracticeTestTemplates WHERE BusinessId IS NULL
UNION ALL SELECT 'MockTests', COUNT(*) FROM MockTests WHERE BusinessId IS NULL
UNION ALL SELECT 'Admins', COUNT(*) FROM Admins WHERE BusinessId IS NULL;

-- 3. No duplicates (expect 0 rows; the unique indexes also make this impossible).
SELECT 'Exams' AS T, BusinessId, COUNT(*) AS N FROM Exams WHERE BusinessId IS NOT NULL GROUP BY BusinessId HAVING COUNT(*) > 1
UNION ALL SELECT 'QuestionBankSubjects', BusinessId, COUNT(*) FROM QuestionBankSubjects WHERE BusinessId IS NOT NULL GROUP BY BusinessId HAVING COUNT(*) > 1
UNION ALL SELECT 'PracticeTestTemplates', BusinessId, COUNT(*) FROM PracticeTestTemplates WHERE BusinessId IS NOT NULL GROUP BY BusinessId HAVING COUNT(*) > 1
UNION ALL SELECT 'MockTests', BusinessId, COUNT(*) FROM MockTests WHERE BusinessId IS NOT NULL GROUP BY BusinessId HAVING COUNT(*) > 1
UNION ALL SELECT 'Admins', BusinessId, COUNT(*) FROM Admins WHERE BusinessId IS NOT NULL GROUP BY BusinessId HAVING COUNT(*) > 1;

-- 4. Every live ID is in the permanent registry, pointing at the same GUID (expect 0 rows).
SELECT 'Exams' AS T, e.Id, e.BusinessId FROM Exams e LEFT JOIN BusinessIdRegistry r ON r.BusinessId = e.BusinessId AND r.EntityId = e.Id WHERE e.BusinessId IS NOT NULL AND r.BusinessId IS NULL
UNION ALL SELECT 'QuestionBankSubjects', s.Id, s.BusinessId FROM QuestionBankSubjects s LEFT JOIN BusinessIdRegistry r ON r.BusinessId = s.BusinessId AND r.EntityId = s.Id WHERE s.BusinessId IS NOT NULL AND r.BusinessId IS NULL
UNION ALL SELECT 'PracticeTestTemplates', t.Id, t.BusinessId FROM PracticeTestTemplates t LEFT JOIN BusinessIdRegistry r ON r.BusinessId = t.BusinessId AND r.EntityId = t.Id WHERE t.BusinessId IS NOT NULL AND r.BusinessId IS NULL
UNION ALL SELECT 'MockTests', m.Id, m.BusinessId FROM MockTests m LEFT JOIN BusinessIdRegistry r ON r.BusinessId = m.BusinessId AND r.EntityId = m.Id WHERE m.BusinessId IS NOT NULL AND r.BusinessId IS NULL
UNION ALL SELECT 'Admins', a.Id, a.BusinessId FROM Admins a LEFT JOIN BusinessIdRegistry r ON r.BusinessId = a.BusinessId AND r.EntityId = a.Id WHERE a.BusinessId IS NOT NULL AND r.BusinessId IS NULL;

-- 5. No counter is BEHIND the highest ID in use for its prefix (expect 0 rows). A counter behind the
--    data would only cost a few skipped numbers (the generator skips taken IDs), but it shouldn't happen.
SELECT c.CounterKey, c.LastNumber, x.MaxInUse
FROM BusinessIdCounters c
JOIN (
    SELECT 'SUB' AS K, MAX(TRY_CONVERT(int, SUBSTRING(BusinessId, 4, 20))) AS MaxInUse FROM QuestionBankSubjects WHERE BusinessId LIKE 'SUB[0-9]%'
    UNION ALL SELECT 'TST', MAX(TRY_CONVERT(int, SUBSTRING(BusinessId, 4, 20))) FROM PracticeTestTemplates WHERE BusinessId LIKE 'TST[0-9]%'
    UNION ALL SELECT 'MCK', MAX(TRY_CONVERT(int, SUBSTRING(BusinessId, 4, 20))) FROM MockTests WHERE BusinessId LIKE 'MCK[0-9]%'
    UNION ALL SELECT 'ADM', MAX(TRY_CONVERT(int, SUBSTRING(BusinessId, 4, 20))) FROM Admins WHERE BusinessId LIKE 'ADM[0-9]%'
) x ON x.K = c.CounterKey
WHERE c.LastNumber < ISNULL(x.MaxInUse, 0);

-- 6. EYEBALL: exam IDs and the organization code each got. Review the codes BEFORE anyone relies on
--    these IDs externally (only a SuperAdmin can change one afterwards).
SELECT e.BusinessId, e.Name, o.Name AS Organization, e.CreatedAt
FROM Exams e LEFT JOIN Organizations o ON o.Id = e.OrganizationId
ORDER BY e.BusinessId;

-- 7. EYEBALL: every ID ever issued, including those whose record has since been deleted/merged away
--    (these are the ones that can never be handed out again).
SELECT r.BusinessId, r.EntityType, r.EntityId, r.IssuedAt,
       CASE WHEN EXISTS (SELECT 1 FROM Exams WHERE Id = r.EntityId)
              OR EXISTS (SELECT 1 FROM QuestionBankSubjects WHERE Id = r.EntityId)
              OR EXISTS (SELECT 1 FROM PracticeTestTemplates WHERE Id = r.EntityId)
              OR EXISTS (SELECT 1 FROM MockTests WHERE Id = r.EntityId)
              OR EXISTS (SELECT 1 FROM Admins WHERE Id = r.EntityId)
            THEN 'live' ELSE 'RETIRED (record gone, ID reserved forever)' END AS State
FROM BusinessIdRegistry r
ORDER BY r.EntityType, r.BusinessId;

-- 8. Audit trail of SuperAdmin changes.
SELECT TOP 50 CreatedAt, AdminId, Action, TargetType, TargetId, Detail
FROM AuditLogs WHERE Action = 'BusinessId.Change' ORDER BY CreatedAt DESC;
