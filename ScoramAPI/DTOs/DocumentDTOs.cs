namespace ScoramAPI.DTOs
{
    // Response envelope for the Documents endpoints specifically (Step 10's exact
    // {success, message, data} shape). Deliberately scoped to this one feature rather than a
    // codebase-wide convention change -- every other controller already has its own established
    // response shape (plain DTOs / NoContent / ProblemDetails), and Step 21 says not to touch
    // unrelated APIs.
    public class DocumentApiResponse<T>
    {
        public bool Success { get; set; }
        public string Message { get; set; } = string.Empty;
        public T? Data { get; set; }
    }

    public class DocumentUploadResponseDto
    {
        public Guid Id { get; set; }
        public string FileName { get; set; } = string.Empty;
        public string ContentType { get; set; } = string.Empty;
        public long FileSize { get; set; }
        public string BlobName { get; set; } = string.Empty;
        public string Category { get; set; } = string.Empty;
        public DateTime UploadedAt { get; set; }
    }
}
