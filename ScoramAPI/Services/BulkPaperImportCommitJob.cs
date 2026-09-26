using ScoramAPI.DTOs;

namespace ScoramAPI.Services
{
    public class BulkPaperImportCommitJob
    {
        public Guid JobId { get; set; }
        public Guid AdminId { get; set; }
        public List<int>? RowNumbers { get; set; }
    }

    // BulkPaperImportController deliberately has no DB job row (see that controller's own top-level
    // comment on why -- there's nothing a bulk "undo" needs to track, each created Paper is already
    // independently visible/deletable), so unlike BulkImportController/QuestionBankAdminController
    // (which already have an ImportJob/QuestionBankImportJob row with a Status column to poll),
    // there's nowhere durable to record "this queued commit is still processing" for this flow. This
    // wrapper is stored in IStagedDataCache instead, under its own key (separate from the
    // preview-rows cache entry, which CommitAsync consumes and deletes as soon as it actually runs) --
    // see BulkPaperImportController for the exact key and TTL.
    public class BulkPaperImportCommitStatus
    {
        public string Status { get; set; } = "Processing"; // "Processing" | "Committed" | "Failed"
        public BulkPaperImportCommitResultDto? Result { get; set; }
        public string? ErrorMessage { get; set; }
    }
}
