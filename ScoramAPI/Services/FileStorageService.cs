using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;

namespace ScoramAPI.Services
{
    public enum DirectMessageKind { Image, Document, Audio }

    public interface IFileStorageService
    {
        /// <summary>Validates and saves an uploaded image under wwwroot/uploads/{subfolder}/, returning
        /// the relative URL to store on the entity (e.g. "/uploads/question-images/{guid}.png"), or
        /// null if no file was given. Throws ArgumentException with a user-facing message on validation
        /// failure (bad extension / too large / empty file) so the controller can turn it into a 400.</summary>
        Task<string?> SaveImageAsync(IFormFile? file, string subfolder);

        /// <summary>Same contract as SaveImageAsync, but for a chat attachment which may be an image OR
        /// a document (PDF/Word/Excel/PowerPoint) -- returns which kind it turned out to be so the
        /// caller can set ChatMessage.MessageType accordingly.</summary>
        Task<(string? url, bool isDocument)> SaveChatAttachmentAsync(IFormFile? file);

        /// <summary>Same idea as SaveChatAttachmentAsync, but for a direct message, which can also be
        /// a voice note. Returns which kind it turned out to be so the caller can set
        /// DirectMessage.MessageType accordingly.</summary>
        Task<(string? url, DirectMessageKind kind)> SaveDirectMessageAttachmentAsync(IFormFile? file);

        /// <summary>Best-effort delete of a previously-saved file, given the relative URL a Save*Async
        /// method returned. Silently does nothing if the URL is null/external/already gone -- deleting
        /// old files is a cleanup nicety, not something that should ever fail a request.</summary>
        void DeleteImage(string? relativeUrl);
    }

    public class FileStorageService : IFileStorageService
    {
        private static readonly string[] ImageExtensions = { ".png", ".jpg", ".jpeg", ".webp", ".svg" };
        private static readonly string[] DocumentExtensions = { ".pdf", ".doc", ".docx", ".xls", ".xlsx", ".ppt", ".pptx" };
        // .webm is what MediaRecorder produces in Chrome/Firefox by default; .m4a/.mp3/.ogg/.wav cover
        // Safari and any recordings uploaded from outside the in-app recorder.
        private static readonly string[] AudioExtensions = { ".webm", ".m4a", ".mp3", ".ogg", ".wav" };
        private const long MaxImageSizeBytes = 5 * 1024 * 1024;   // 5 MB -- question diagrams can be a bit larger than a logo
        private const long MaxDocumentSizeBytes = 15 * 1024 * 1024; // 15 MB -- notes/PDFs shared in chat
        private const long MaxAudioSizeBytes = 10 * 1024 * 1024;  // 10 MB -- generous for a voice note (~10+ min at typical bitrates)

        private readonly IWebHostEnvironment _env;

        public FileStorageService(IWebHostEnvironment env)
        {
            _env = env;
        }

        public Task<string?> SaveImageAsync(IFormFile? file, string subfolder) =>
            SaveFileAsync(file, subfolder, ImageExtensions, MaxImageSizeBytes, "Images");

        public async Task<(string? url, bool isDocument)> SaveChatAttachmentAsync(IFormFile? file)
        {
            if (file == null) return (null, false);

            var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
            var isDocument = DocumentExtensions.Contains(ext);
            var isImage = ImageExtensions.Contains(ext);

            if (!isDocument && !isImage)
                throw new ArgumentException(
                    $"Attachments must be an image ({string.Join(", ", ImageExtensions)}) or a document ({string.Join(", ", DocumentExtensions)}).");

            var maxSize = isDocument ? MaxDocumentSizeBytes : MaxImageSizeBytes;
            var extensions = isDocument ? DocumentExtensions : ImageExtensions;
            var label = isDocument ? "Documents" : "Images";
            var url = await SaveFileAsync(file, "chat-attachments", extensions, maxSize, label);
            return (url, isDocument);
        }

        public async Task<(string? url, DirectMessageKind kind)> SaveDirectMessageAttachmentAsync(IFormFile? file)
        {
            if (file == null) return (null, DirectMessageKind.Image);

            var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
            var isDocument = DocumentExtensions.Contains(ext);
            var isImage = ImageExtensions.Contains(ext);
            var isAudio = AudioExtensions.Contains(ext);

            if (!isDocument && !isImage && !isAudio)
                throw new ArgumentException(
                    $"Attachments must be an image ({string.Join(", ", ImageExtensions)}), a document ({string.Join(", ", DocumentExtensions)}), or an audio recording ({string.Join(", ", AudioExtensions)}).");

            var (extensions, maxSize, label, kind) = isAudio
                ? (AudioExtensions, MaxAudioSizeBytes, "Voice notes", DirectMessageKind.Audio)
                : isDocument
                    ? (DocumentExtensions, MaxDocumentSizeBytes, "Documents", DirectMessageKind.Document)
                    : (ImageExtensions, MaxImageSizeBytes, "Images", DirectMessageKind.Image);

            var url = await SaveFileAsync(file, "dm-attachments", extensions, maxSize, label);
            return (url, kind);
        }

        private async Task<string?> SaveFileAsync(IFormFile? file, string subfolder, string[] allowedExtensions, long maxSizeBytes, string kindLabel)
        {
            if (file == null) return null;
            if (file.Length == 0) throw new ArgumentException("An uploaded file is empty.");
            if (file.Length > maxSizeBytes) throw new ArgumentException($"{kindLabel} must be {maxSizeBytes / (1024 * 1024)} MB or smaller.");

            var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
            if (!allowedExtensions.Contains(ext))
                throw new ArgumentException($"{kindLabel} must be one of: {string.Join(", ", allowedExtensions)}.");

            // Never trust the original filename -- generate our own to avoid path traversal / collisions.
            var fileName = $"{Guid.NewGuid()}{ext}";
            var uploadsDir = Path.Combine(_env.WebRootPath ?? "wwwroot", "uploads", subfolder);
            Directory.CreateDirectory(uploadsDir);

            var fullPath = Path.Combine(uploadsDir, fileName);
            using (var stream = new FileStream(fullPath, FileMode.Create))
            {
                await file.CopyToAsync(stream);
            }

            return $"/uploads/{subfolder}/{fileName}";
        }

        public void DeleteImage(string? relativeUrl)
        {
            if (string.IsNullOrWhiteSpace(relativeUrl) || !relativeUrl.StartsWith("/uploads/")) return;

            try
            {
                var fullPath = Path.Combine(_env.WebRootPath ?? "wwwroot", relativeUrl.TrimStart('/').Replace('/', Path.DirectorySeparatorChar));
                if (File.Exists(fullPath)) File.Delete(fullPath);
            }
            catch
            {
                // Best-effort cleanup -- an orphaned file on disk is a non-issue, but failing the
                // request over it (e.g. a locked file, permissions) would be a worse outcome.
            }
        }
    }
}
