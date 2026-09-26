namespace ScoramAPI.Services
{
    public class QuestionBankImportCommitJob
    {
        public Guid JobId { get; set; }
        public Guid AdminId { get; set; }
        public List<int>? RowNumbers { get; set; }
    }
}
