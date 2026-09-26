namespace ScoramAPI.Services
{
    public enum SearchIndexJobType
    {
        // Re-fetches the paper's CURRENT questions at processing time and indexes them -- not a
        // snapshot taken at enqueue time, so if the paper is edited again before this job is picked
        // up, the worker indexes whatever is actually there when it runs. Standard eventual-
        // consistency tradeoff for background work; the alternative (serializing a point-in-time
        // snapshot into the job) risks indexing stale content if edits land in between.
        IndexPaper,

        // Removes exactly these IDs -- unlike IndexPaper, there's nothing to re-fetch here: an
        // Unpublish keeps the questions but they should no longer be searchable, and a Delete may
        // have already removed the rows entirely by the time this job runs, so the IDs have to be
        // captured at enqueue time, before the DB change that makes them unrecoverable.
        RemoveQuestions
    }

    public class SearchIndexJob
    {
        public SearchIndexJobType JobType { get; set; }
        public Guid? PaperId { get; set; }
        public List<Guid>? QuestionIds { get; set; }
    }
}
