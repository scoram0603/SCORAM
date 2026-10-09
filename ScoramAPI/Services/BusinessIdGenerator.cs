using Microsoft.EntityFrameworkCore;
using ScoramAPI.Data;
using ScoramAPI.Models;

namespace ScoramAPI.Services
{
    // The one place Business IDs are actually issued. Used by ScoramDbContext.SaveChangesAsync (every
    // newly inserted Exam/Subject/Test/Mock Test/Admin, wherever in the code base it was created --
    // controllers, bulk imports, background services) and by the one-time backfill of existing rows.
    //
    // HOW IT STAYS SAFE
    //   * Concurrency: the sequence number comes from a row in BusinessIdCounters that is incremented
    //     with a single atomic MERGE ... UPDATE inside the SAME transaction as the INSERT that uses
    //     it. That UPDATE takes an exclusive lock on the counter row that is held until commit, so two
    //     admins creating "an SSC exam" at the same instant are serialised: A gets 004, B waits, then
    //     gets 005. There is no SELECT MAX(...)+1 anywhere.
    //   * Never reused: counters only ever go up (a delete doesn't touch them), AND every issued ID
    //     is written to BusinessIdRegistry, whose primary key means the same ID can never be inserted
    //     twice -- even after its record is deleted/merged away, or after a SuperAdmin renumbers it.
    //   * If the surrounding transaction rolls back, the counter increment and the registry row roll
    //     back with it, so a failed create doesn't leave a gap-with-a-ghost or a burned number.
    internal static class BusinessIdGenerator
    {
        // Sentinel row in BusinessIdCounters -- present once the one-time backfill of pre-existing
        // records has finished. Until then new records are left with a null BusinessId (they are
        // picked up, in creation order, by the backfill) so that pre-existing records still get the
        // low numbers (EXMSSC001...) in the order they were created rather than being pushed behind
        // whatever was created between deploy and backfill.
        public const string BackfillCompleteKey = "__BACKFILL_COMPLETE__";

        // Once true it stays true for the life of the process (the backfill is one-way), which keeps
        // the hot path (every insert of one of these entities) free of an extra query.
        private static volatile bool _backfillKnownComplete;

        public static BusinessIdEntityType? TypeOf(object entity) => entity switch
        {
            Exam => BusinessIdEntityType.Exam,
            QuestionBankSubject => BusinessIdEntityType.Subject,
            PracticeTestTemplate => BusinessIdEntityType.Test,
            MockTest => BusinessIdEntityType.MockTest,
            Admin => BusinessIdEntityType.Admin,
            SharedStimulus => BusinessIdEntityType.SharedStimulus,
            _ => null
        };

        public static async Task<bool> IsBackfillCompleteAsync(ScoramDbContext db, CancellationToken ct)
        {
            if (_backfillKnownComplete) return true;
            var done = await db.BusinessIdCounters.AsNoTracking().AnyAsync(c => c.CounterKey == BackfillCompleteKey, ct);
            if (done) _backfillKnownComplete = true;
            return done;
        }

        public static async Task MarkBackfillCompleteAsync(ScoramDbContext db, CancellationToken ct)
        {
            await db.Database.ExecuteSqlInterpolatedAsync($@"
                IF NOT EXISTS (SELECT 1 FROM BusinessIdCounters WHERE CounterKey = {BackfillCompleteKey})
                    INSERT INTO BusinessIdCounters (CounterKey, LastNumber) VALUES ({BackfillCompleteKey}, 1);", ct);
        }

        // Only called by the backfill service after its transaction has committed.
        public static void RememberBackfillComplete() => _backfillKnownComplete = true;

        // Assigns a BusinessId to every entity in the list (in the order given). MUST run inside an
        // open transaction -- see the class comment for why.
        public static async Task AssignAsync(ScoramDbContext db, IReadOnlyList<IHasBusinessId> entities, CancellationToken ct)
        {
            foreach (var entity in entities)
            {
                var type = TypeOf(entity)
                    ?? throw new InvalidOperationException($"{entity.GetType().Name} does not take a Business ID.");

                string? orgCode = null;
                if (type == BusinessIdEntityType.Exam)
                    orgCode = await ResolveExamOrganizationCodeAsync(db, (Exam)entity, ct);

                entity.BusinessId = await IssueAsync(db, type, orgCode, entity.Id, ct);
            }
        }

        private static async Task<string> ResolveExamOrganizationCodeAsync(ScoramDbContext db, Exam exam, CancellationToken ct)
        {
            var orgName = exam.Organization?.Name;
            if (orgName == null && exam.OrganizationId.HasValue)
            {
                orgName = await db.Organizations.AsNoTracking()
                    .Where(o => o.Id == exam.OrganizationId.Value)
                    .Select(o => o.Name)
                    .FirstOrDefaultAsync(ct);
            }
            return ExamOrganizationCodes.Resolve(orgName, exam.Name);
        }

        // Takes the next free number for this counter, records it in the registry (tracked -- it is
        // written by the caller's SaveChanges, in the same batch as the entity itself) and returns it.
        private static async Task<string> IssueAsync(
            ScoramDbContext db, BusinessIdEntityType type, string? orgCode, Guid entityId, CancellationToken ct)
        {
            var counterKey = BusinessIdFormats.CounterKeyFor(type, orgCode);

            // The loop only iterates when the next sequential number is already taken -- which can
            // only happen if a SuperAdmin manually assigned that exact ID earlier (e.g. moved
            // EXMSSC001 to EXMSSC010: when the counter reaches 010 it must skip past it). Bounded so a
            // corrupted counter can never spin forever.
            for (var attempt = 0; attempt < 5000; attempt++)
            {
                var number = await NextNumberAsync(db, counterKey, ct);
                var candidate = BusinessIdFormats.Format(type, number, orgCode);

                var taken = db.BusinessIdRegistry.Local.Any(r => r.BusinessId == candidate)
                    || await db.BusinessIdRegistry.AsNoTracking().AnyAsync(r => r.BusinessId == candidate, ct);
                if (taken) continue;

                db.BusinessIdRegistry.Add(new BusinessIdRegistryEntry
                {
                    BusinessId = candidate,
                    EntityType = type.ToString(),
                    EntityId = entityId,
                    IssuedAt = DateTime.UtcNow
                });
                return candidate;
            }

            throw new InvalidOperationException($"Could not find a free Business ID for counter '{counterKey}'.");
        }

        // Atomic increment-and-read. MERGE ... WITH (HOLDLOCK) makes "create the counter row if this
        // is the first ID for a brand-new organization code, otherwise add 1" a single race-free
        // statement (two first-ever SSC-style creates can't both INSERT the same key), and the row
        // lock it takes is held until the surrounding transaction ends.
        private static async Task<int> NextNumberAsync(ScoramDbContext db, string counterKey, CancellationToken ct)
        {
            if (db.Database.CurrentTransaction == null)
                throw new InvalidOperationException("Business IDs must be issued inside a transaction.");

            await db.Database.ExecuteSqlInterpolatedAsync($@"
                MERGE BusinessIdCounters WITH (HOLDLOCK) AS t
                USING (SELECT {counterKey} AS CounterKey) AS s ON t.CounterKey = s.CounterKey
                WHEN MATCHED THEN UPDATE SET LastNumber = t.LastNumber + 1
                WHEN NOT MATCHED THEN INSERT (CounterKey, LastNumber) VALUES (s.CounterKey, 1);", ct);

            return await db.Database
                .SqlQuery<int>($"SELECT LastNumber AS Value FROM BusinessIdCounters WHERE CounterKey = {counterKey}")
                .SingleAsync(ct);
        }
    }
}
