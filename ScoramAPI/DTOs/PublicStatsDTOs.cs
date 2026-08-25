namespace ScoramAPI.DTOs
{
    // LANDING PAGE -- see PublicStatsController. Three honest numbers, nothing else.
    public class PublicStatsDto
    {
        public int TotalQuestions { get; set; }
        public int TotalExams { get; set; }
        public int TotalStudents { get; set; }
    }
}
