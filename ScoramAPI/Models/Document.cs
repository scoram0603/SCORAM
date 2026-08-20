using System.ComponentModel.DataAnnotations;
using ScoramAPI.Enums;

namespace ScoramAPI.Models
{
    // AZURE BLOB STORAGE (Documents) -- SQL-side metadata row for a file whose actual bytes live in
    // Azure Blob Storage (private "uploads" container), never in this database. One row per uploaded
    // file across every use case in Step 15 (question images, PYQ/exam PDFs, student profile images);
    // Category (see Enums/DocumentCategory) is what tells them apart and which virtual blob folder
    // they live under.
    //
    // UploadedByUserId / UploadedByAdminId is the same dual-nullable-FK reuse pattern already used
    // throughout ScoramDbContext (QuestionSolution, QuestionComment, QuestionReport, ...) so this one
    // table can be uploaded to by either a Student (ProfileImage) or an Admin/SuperAdmin
    // (QuestionImage/PyqPdf/ExamPdf) -- exactly one of the two is ever set, enforced in
    // DocumentsController rather than at the DB level (same as every other dual-FK entity here).
    public class Document
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        // Original filename as the uploader's browser sent it -- shown back to them and used as the
        // Content-Disposition filename on download. Never used to build the blob name (see BlobName).
        [Required, MaxLength(260)]
        public string FileName { get; set; } = string.Empty;

        // Storage key inside the container, e.g. "question-images/550e8400-....jpg" -- globally
        // unique (Guid-based, see AzureBlobService.BuildBlobName), never the original filename.
        [Required, MaxLength(300)]
        public string BlobName { get; set; } = string.Empty;

        [Required, MaxLength(100)]
        public string ContentType { get; set; } = string.Empty;

        public long FileSize { get; set; }

        public DocumentCategory Category { get; set; }

        // Deliberately left null for now -- the container is PRIVATE (Step 16), so a raw blob URL
        // isn't independently usable without a SAS token anyway. All access goes through
        // /api/documents/{id}/view and /download, which stream the file through this API. Kept as a
        // nullable column (matching the metadata shape requested) so a future SAS-URL feature can
        // populate it without a schema change.
        public string? FileUrl { get; set; }

        public DateTime UploadedAt { get; set; } = DateTime.UtcNow;

        // Exactly one of these two is set, matching who was authenticated at upload time -- see the
        // class-level comment above.
        public Guid? UploadedByUserId { get; set; }
        public User? UploadedByUser { get; set; }

        public Guid? UploadedByAdminId { get; set; }
        public Admin? UploadedByAdmin { get; set; }
    }
}
