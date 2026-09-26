using System.ComponentModel.DataAnnotations;
using ScoramAPI.Enums;

namespace ScoramAPI.Models
{
    // One row per bulk-import attempt against a Paper. Created at preview time (before anything is
    // actually written to Questions) so a review that never gets committed still shows up in history
    // -- "someone tried an import and abandoned it" is useful information, not just successful ones.
    //
    // The parsed-but-not-yet-committed rows themselves are NOT stored here (or anywhere durable) --
    // they live briefly in IStagedDataCache between preview and commit (see BulkImportController).
    // That cache is Redis-backed when ConnectionStrings:Redis is configured (see Program.cs), so a
    // preview and its later commit landing on different app instances behind a load balancer is fine
    // either way; without Redis configured, it falls back to an in-memory-only cache and this
    // reverts to a single-instance tradeoff -- an app restart between preview and commit loses the
    // review and it needs re-uploading. A future move to a real job queue/durable storage wouldn't
    // need to change this table's shape.
    public class ImportJob
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        public Guid PaperId { get; set; }
        public Paper? Paper { get; set; }

        public Guid CreatedByAdminId { get; set; }
        public Admin? CreatedByAdmin { get; set; }

        [MaxLength(255)]
        public string FileName { get; set; } = string.Empty;

        public ImportFileFormat Format { get; set; }

        public ImportJobStatus Status { get; set; } = ImportJobStatus.PendingReview;

        public int TotalRows { get; set; }
        public int ValidRows { get; set; }
        public int InvalidRows { get; set; }

        // Set once Committed -- the actual number of questions written (may be less than ValidRows if
        // the admin excluded some valid-but-unwanted rows at commit time; see BulkImportCommitDto).
        public int ImportedCount { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? CommittedAt { get; set; }
        public DateTime? RolledBackAt { get; set; }
    }
}
