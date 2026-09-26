namespace ScoramAPI.Services
{
    // AdminId is carried explicitly rather than read from a ClaimsPrincipal at processing time --
    // there is no HTTP request/User by the time SearchIndexWorker or BulkImportCommitWorker actually
    // handles this, only whatever was captured when it was enqueued.
    public class BulkImportCommitJob
    {
        public Guid JobId { get; set; }
        public Guid AdminId { get; set; }
        public List<int>? RowNumbers { get; set; }
    }
}
