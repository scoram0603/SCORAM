using System.ComponentModel.DataAnnotations;

namespace ScoramAPI.Models
{
    // ==================================================================================
    // BUSINESS IDs -- human-readable administrative identifiers (EXMSSC001, SUB001, TST0001,
    // MCK0001, ADM0001) that sit ALONGSIDE the existing GUID Id of these five entities.
    //
    //   Id           (Guid)   = internal identity: primary key, every foreign key, every join.
    //                           Never replaced, never rewritten.
    //   BusinessId   (string) = what admins say, search for, put in tickets and reports.
    //                           Never used as a key or foreign key anywhere.
    //
    // Nullable on purpose: existing production rows have no value until the one-time backfill
    // (see Services/BusinessIdService.BackfillAsync) has run, and the unique index is filtered to
    // non-null values so it can be created before that. New rows are assigned automatically in
    // ScoramDbContext.SaveChangesAsync -- callers never set this themselves.
    // ==================================================================================
    public interface IHasBusinessId
    {
        Guid Id { get; }
        string? BusinessId { get; set; }
    }

    public enum BusinessIdEntityType
    {
        Exam,
        Subject,
        Test,
        MockTest,
        Admin,
        SharedStimulus
    }

    // One row per counter (e.g. "EXM:SSC", "SUB", "TST", "MCK", "ADM"). LastNumber only ever goes
    // UP -- it is incremented atomically inside the same transaction as the insert that consumes
    // it (see BusinessIdGenerator.NextNumberAsync) and is never decremented by a delete, which is
    // exactly what stops a deleted record's number being handed out again.
    public class BusinessIdCounter
    {
        [Key, MaxLength(50)]
        public string CounterKey { get; set; } = string.Empty;

        public int LastNumber { get; set; }
    }

    // Permanent ledger of EVERY Business ID that has ever been issued -- including IDs whose
    // record was later deleted, merged away, or given a different ID by a SuperAdmin. Its primary
    // key is what makes "never reuse" a database guarantee rather than a convention: an ID that is
    // in here can never be issued a second time to a different record.
    //
    // Deliberately no foreign key to the entity table (EntityId is a plain Guid): the whole point
    // is that the row must outlive the record it was issued to.
    public class BusinessIdRegistryEntry
    {
        [Key, MaxLength(40)]
        public string BusinessId { get; set; } = string.Empty;

        // BusinessIdEntityType, stored as text so the table stays readable in SSMS.
        [Required, MaxLength(20)]
        public string EntityType { get; set; } = string.Empty;

        // GUID of the record this ID was issued to. Kept after that record is deleted.
        public Guid EntityId { get; set; }

        public DateTime IssuedAt { get; set; } = DateTime.UtcNow;
    }
}
