namespace ScoramAPI.DTOs
{
    // SuperAdmin-only "correct a Business ID" request. Deliberately carries the entity's GUID (the
    // real identity) rather than its current Business ID, so the request can't be misdirected by a
    // typo in the ID being replaced.
    public class BusinessIdChangeRequestDto
    {
        // exam | subject | test | mocktest | admin (case/hyphen-insensitive; plural accepted)
        public string EntityType { get; set; } = string.Empty;
        public Guid EntityId { get; set; }
        public string NewBusinessId { get; set; } = string.Empty;
        // Must be true -- the "Change Business ID?" confirmation. Enforced here, not just in the UI.
        public bool Confirm { get; set; }
    }

    public class BusinessIdChangeResultDto
    {
        public string Message { get; set; } = string.Empty;
        public string EntityType { get; set; } = string.Empty;
        // The record's GUID -- unchanged by this operation, as are all foreign keys pointing at it.
        public Guid EntityId { get; set; }
        public string Name { get; set; } = string.Empty;
        public string? OldBusinessId { get; set; }
        public string NewBusinessId { get; set; } = string.Empty;
    }

    public class BusinessIdBackfillItemDto
    {
        public string EntityType { get; set; } = string.Empty;
        public Guid EntityId { get; set; }
        public string Name { get; set; } = string.Empty;
        public string BusinessId { get; set; } = string.Empty;
    }

    public class BusinessIdBackfillResultDto
    {
        // true = ran inside a transaction that was rolled back; nothing was saved.
        public bool DryRun { get; set; }
        // true once the sentinel says the one-time backfill has been completed for real.
        public bool Completed { get; set; }
        public int Exams { get; set; }
        public int Subjects { get; set; }
        public int Tests { get; set; }
        public int MockTests { get; set; }
        public int Admins { get; set; }
        public int Total => Exams + Subjects + Tests + MockTests + Admins;
        public List<BusinessIdBackfillItemDto> Items { get; set; } = new();
    }
}
