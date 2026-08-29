using Microsoft.AspNetCore.Http;

namespace ScoramAPI.Services
{
    public enum DirectMessageKind { Image, Document, Audio }

    public interface IFileStorageService
    {
        /// <summary>Validates and saves an uploaded image to Azure Blob Storage under
        /// "{subfolder}/{guid}{ext}" in the private "uploads" container, returning a relative URL to
        /// store on the entity (e.g. "/uploads/question-images/{guid}.png"), or null if no file was
        /// given. That URL is served by UploadedFilesController, which streams the matching blob back
        /// -- so callers of this interface don't need to know or care that the bytes live in Azure
        /// rather than on local disk. Throws ArgumentException with a user-facing message on
        /// validation failure (bad extension / too large / empty file) so the controller can turn it
        /// into a 400.</summary>
        Task<string?> SaveImageAsync(IFormFile? file, string subfolder);

        /// <summary>Same validation/behavior as SaveImageAsync, but for a raw byte stream with a
        /// known original filename and length instead of an IFormFile -- used when staging an image
        /// that came from inside an uploaded ZIP (bulk import), where there's no IFormFile to begin
        /// with, only bytes already read out of a ZipArchiveEntry. Throws ArgumentException on the
        /// same validation failures as SaveImageAsync (bad extension / too large / empty).</summary>
        Task<string?> SaveImageFromStreamAsync(Stream stream, string originalFileName, long length, string subfolder);

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
        /// old files is a cleanup nicety, not something that should ever fail a request. Async because
        /// deleting a blob is a network call (unlike the old local-disk File.Delete) -- callers should
        /// await it, but a failure here is swallowed rather than thrown, same as before.</summary>
        Task DeleteImageAsync(string? relativeUrl);

        /// <summary>Physically copies an already-uploaded image to a new blob under the given
        /// subfolder, returning the new file's own relative URL (or null if sourceRelativeUrl is
        /// null/not a local upload). Used when two independently-editable records need to end up
        /// with "the same picture" (e.g. QuestionBankMirrorService mirroring a PYQ question's images)
        /// -- giving each its own physical file means deleting/replacing one's image can never break
        /// the other's, the way sharing a single URL between two records would.</summary>
        Task<string?> CopyImageAsync(string? sourceRelativeUrl, string subfolder);

        /// <summary>Best-effort delete of every blob under a given subfolder (e.g.
        /// "bulk-import-staging/{jobId}") -- used to clean up a bulk-import job's temporary staged
        /// images once they're no longer needed (after commit, each image that made it into a
        /// question has already been copied elsewhere via CopyImageAsync; for an
        /// abandoned/expired preview, none of them are needed at all). Never throws -- same
        /// "cleanup nicety, not a request-failing concern" contract as DeleteImageAsync.</summary>
        Task DeleteFolderAsync(string subfolder);
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

        private const string UploadsUrlPrefix = "/uploads/";

        private static readonly Dictionary<string, string> ContentTypeByExtension = new()
        {
            [".png"] = "image/png",
            [".jpg"] = "image/jpeg",
            [".jpeg"] = "image/jpeg",
            [".webp"] = "image/webp",
            [".svg"] = "image/svg+xml",
            [".pdf"] = "application/pdf",
            [".doc"] = "application/msword",
            [".docx"] = "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            [".xls"] = "application/vnd.ms-excel",
            [".xlsx"] = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            [".ppt"] = "application/vnd.ms-powerpoint",
            [".pptx"] = "application/vnd.openxmlformats-officedocument.presentationml.presentation",
            [".webm"] = "audio/webm",
            [".m4a"] = "audio/mp4",
            [".mp3"] = "audio/mpeg",
            [".ogg"] = "audio/ogg",
            [".wav"] = "audio/wav",
        };

        private readonly IAzureBlobService _blobService;

        public FileStorageService(IAzureBlobService blobService)
        {
            _blobService = blobService;
        }

        public Task<string?> SaveImageAsync(IFormFile? file, string subfolder) =>
            SaveFileAsync(file, subfolder, ImageExtensions, MaxImageSizeBytes, "Images");

        public Task<string?> SaveImageFromStreamAsync(Stream stream, string originalFileName, long length, string subfolder) =>
            SaveStreamAsync(stream, originalFileName, length, subfolder, ImageExtensions, MaxImageSizeBytes, "Images");

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
            await using var stream = file.OpenReadStream();
            return await SaveStreamAsync(stream, file.FileName, file.Length, subfolder, allowedExtensions, maxSizeBytes, kindLabel);
        }

        // The actual validate-then-upload logic, shared by the IFormFile-based path above (the
        // normal multipart-form upload case) and SaveImageFromStreamAsync (bytes already extracted
        // from a ZIP entry, with no IFormFile wrapper). Behavior is identical either way -- same
        // extension/size checks, same GUID-based blob naming, same "return the relative /uploads/...
        // URL" contract.
        private async Task<string?> SaveStreamAsync(Stream stream, string originalFileName, long length, string subfolder, string[] allowedExtensions, long maxSizeBytes, string kindLabel)
        {
            if (length == 0) throw new ArgumentException("An uploaded file is empty.");
            if (length > maxSizeBytes) throw new ArgumentException($"{kindLabel} must be {maxSizeBytes / (1024 * 1024)} MB or smaller.");

            var ext = Path.GetExtension(originalFileName).ToLowerInvariant();
            if (!allowedExtensions.Contains(ext))
                throw new ArgumentException($"{kindLabel} must be one of: {string.Join(", ", allowedExtensions)}.");

            // Never trust the original filename -- generate our own to avoid path traversal / collisions.
            var fileName = $"{Guid.NewGuid()}{ext}";
            var blobName = $"{subfolder}/{fileName}";

            await _blobService.UploadAsync(blobName, stream, GetContentType(ext));

            return $"{UploadsUrlPrefix}{subfolder}/{fileName}";
        }

        public async Task DeleteImageAsync(string? relativeUrl)
        {
            if (string.IsNullOrWhiteSpace(relativeUrl) || !relativeUrl.StartsWith(UploadsUrlPrefix)) return;

            try
            {
                var blobName = relativeUrl[UploadsUrlPrefix.Length..];
                await _blobService.DeleteAsync(blobName);
            }
            catch
            {
                // Best-effort cleanup -- an orphaned blob is a non-issue, but failing the request
                // over it (e.g. a transient storage hiccup) would be a worse outcome.
            }
        }

        public async Task<string?> CopyImageAsync(string? sourceRelativeUrl, string subfolder)
        {
            if (string.IsNullOrWhiteSpace(sourceRelativeUrl) || !sourceRelativeUrl.StartsWith(UploadsUrlPrefix)) return null;

            var sourceBlobName = sourceRelativeUrl[UploadsUrlPrefix.Length..];

            Stream? sourceStream;
            try
            {
                sourceStream = await _blobService.DownloadAsync(sourceBlobName);
            }
            catch
            {
                return null;
            }
            if (sourceStream == null) return null;

            var ext = Path.GetExtension(sourceBlobName);
            var fileName = $"{Guid.NewGuid()}{ext}";
            var destBlobName = $"{subfolder}/{fileName}";

            await using (sourceStream)
            {
                await _blobService.UploadAsync(destBlobName, sourceStream, GetContentType(ext));
            }

            return $"{UploadsUrlPrefix}{subfolder}/{fileName}";
        }

        private static string GetContentType(string extension) =>
            ContentTypeByExtension.TryGetValue(extension, out var contentType) ? contentType : "application/octet-stream";

        public async Task DeleteFolderAsync(string subfolder)
        {
            if (string.IsNullOrWhiteSpace(subfolder)) return;

            try
            {
                var prefix = subfolder.TrimEnd('/') + "/";
                await _blobService.DeleteByPrefixAsync(prefix);
            }
            catch
            {
                // Best-effort cleanup, same contract as DeleteImageAsync -- an abandoned staging
                // folder is a non-issue, but failing the caller's request over it would be worse.
            }
        }
    }
}
