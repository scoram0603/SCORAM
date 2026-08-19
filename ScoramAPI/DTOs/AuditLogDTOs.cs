namespace ScoramAPI.DTOs
{
    public class AuditLogResponseDto
    {
        public Guid Id { get; set; }
        public Guid AdminId { get; set; }
        public string AdminName { get; set; } = string.Empty;
        public string Action { get; set; } = string.Empty;
        public string? TargetType { get; set; }
        public Guid? TargetId { get; set; }
        public string? Detail { get; set; }
        public DateTime CreatedAt { get; set; }
    }
}
