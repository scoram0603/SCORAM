namespace ScoramAPI.DTOs
{
    // "MY EXAMS" -- see Controllers/UserExamsController.cs.

    public class UserExamPreferenceDto
    {
        public Guid ExamId { get; set; }
        public string ExamName { get; set; } = string.Empty;
        public string? ExamLogoUrl { get; set; }
        public bool IsPrimary { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    // GET /api/user/exams response. An empty Exams list is exactly the "not configured yet" signal
    // the web/Flutter clients use to decide whether to show onboarding (spec section 4) -- no
    // separate "HasConfigured" flag needed.
    public class MyExamsResponseDto
    {
        public List<UserExamPreferenceDto> Exams { get; set; } = new();
        public Guid? PrimaryExamId { get; set; }
    }

    // PUT /api/user/exams -- full replace, used by the onboarding screen ("Continue") and Profile ->
    // "Update My Exams". An empty list is allowed and clears My Exams.
    public class SetMyExamsDto
    {
        public List<Guid> ExamIds { get; set; } = new();

        // Optional -- if omitted, the previous Primary Exam is kept when it's still in the new
        // list, otherwise the first exam in ExamIds becomes Primary (a non-empty selection always
        // ends up with exactly one Primary, per the filtered unique index in ScoramDbContext).
        public Guid? PrimaryExamId { get; set; }
    }
}
