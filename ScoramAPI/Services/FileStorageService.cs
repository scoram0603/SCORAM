using Microsoft.AspNetCore.Http;
using System.Text;
using System.Xml;

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
        /// validation failure: bad extension, too large, empty file, content that doesn't match its
        /// extension's magic bytes (see HasValidMagicBytes), or -- for .svg specifically -- content
        /// that isn't parseable as XML/isn't an &lt;svg&gt; document (see SanitizeSvg; a parseable one
        /// is sanitized and stored, not rejected). The controller turns any of these into a 400.</summary>
        Task<string?> SaveImageAsync(IFormFile? file, string subfolder);

        /// <summary>Same validation/behavior as SaveImageAsync (including magic-byte checking and SVG
        /// sanitization), but for a raw byte stream with a known original filename and length instead
        /// of an IFormFile -- used when staging an image that came from inside an uploaded ZIP (bulk
        /// import), where there's no IFormFile to begin with, only bytes already read out of a
        /// ZipArchiveEntry. Throws ArgumentException on the same validation failures as SaveImageAsync.</summary>
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

        // Content-sniffing signatures ("magic bytes") -- checked in addition to the extension check
        // above, so a file renamed to look like an allowed type (e.g. a script renamed to .png, or an
        // .exe renamed to .jpg) doesn't get accepted just because the extension matched. Formats with
        // no single fixed leading signature (the audio types, and WEBP which needs its own two-part
        // check below) aren't listed here -- see HasValidMagicBytes for how those are handled. SVG is
        // deliberately absent too: it's plain XML text with no magic bytes to check, and the real
        // concern for it is embedded script content, not a spoofed extension -- see SanitizeSvg.
        private static readonly Dictionary<string, byte[][]> MagicBytesByExtension = new()
        {
            [".png"] = new[] { new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A } },
            [".jpg"] = new[] { new byte[] { 0xFF, 0xD8, 0xFF } },
            [".jpeg"] = new[] { new byte[] { 0xFF, 0xD8, 0xFF } },
            [".pdf"] = new[] { new byte[] { 0x25, 0x50, 0x44, 0x46 } }, // "%PDF"
            // Legacy OLE2 Compound File signature -- shared by .doc/.xls/.ppt, there's no way to tell
            // them apart from bytes alone (that would need parsing the compound file's internal
            // streams), so this only confirms "this is some OLE2 Office file", not which one.
            [".doc"] = new[] { new byte[] { 0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1 } },
            [".xls"] = new[] { new byte[] { 0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1 } },
            [".ppt"] = new[] { new byte[] { 0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1 } },
            // Modern OOXML formats are all just ZIP files internally -- same caveat as above, this
            // confirms "a ZIP", not specifically "a valid .xlsx" vs ".docx" vs ".pptx".
            [".docx"] = new[] { new byte[] { 0x50, 0x4B, 0x03, 0x04 } },
            [".xlsx"] = new[] { new byte[] { 0x50, 0x4B, 0x03, 0x04 } },
            [".pptx"] = new[] { new byte[] { 0x50, 0x4B, 0x03, 0x04 } },
        };

        // Elements that can execute script or load arbitrary embedded content -- removed outright
        // rather than sanitized in place, since there's no safe subset of e.g. <script> to keep.
        private static readonly HashSet<string> SvgDangerousElements = new(StringComparer.OrdinalIgnoreCase)
        {
            "script", "foreignObject", "iframe", "embed", "object", "audio", "video", "use"
            // "use" is included even though it's normally an internal-reference element
            // (<use href="#someId">) -- it can also point at an external SVG URL, which would fetch
            // and inline content this sanitizer never got a chance to look at. Internal same-document
            // references are a legitimate, common SVG pattern (e.g. reusing a <defs> icon), but
            // stripping <use> entirely is the only way to rule out the external-reference case without
            // writing a second parser just for its href syntax -- an acceptable tradeoff for
            // logos/diagrams, which don't typically rely on it.
        };

        internal static bool HasValidMagicBytes(string ext, byte[] content)
        {
            if (ext == ".webp")
            {
                // RIFF....WEBP -- bytes 4-7 are the chunk size (varies), so only the two fixed parts
                // around it are checked.
                return content.Length >= 12
                    && content[0] == (byte)'R' && content[1] == (byte)'I' && content[2] == (byte)'F' && content[3] == (byte)'F'
                    && content[8] == (byte)'W' && content[9] == (byte)'E' && content[10] == (byte)'B' && content[11] == (byte)'P';
            }

            if (!MagicBytesByExtension.TryGetValue(ext, out var signatures))
                return true; // no known fixed signature for this extension (e.g. the audio formats)

            foreach (var signature in signatures)
            {
                if (content.Length >= signature.Length && content.AsSpan(0, signature.Length).SequenceEqual(signature))
                    return true;
            }
            return false;
        }

        // Parses the uploaded bytes as XML and strips anything that could execute script or reach
        // outside the file -- rather than trying to blocklist-match "bad" text with regex, which is
        // exactly the kind of thing that's easy to bypass with alternate encodings, whitespace tricks,
        // or namespace prefixes. Throws ArgumentException (same as the rest of this file's validation)
        // if the content isn't parseable as XML, isn't actually an <svg> document, or fails to parse
        // safely -- there's no "best effort" partial sanitization path; either it comes out clean or
        // the upload is rejected.
        internal static byte[] SanitizeSvg(byte[] content)
        {
            // DtdProcessing.Prohibit + a null XmlResolver together rule out both classic XXE (reading
            // local files via an external entity) and XML bombs (billions-of-laughs style entity
            // expansion) -- .NET throws immediately on encountering any DOCTYPE at all under this
            // configuration, which for an SVG (which never legitimately needs one) is exactly the
            // right outcome: reject rather than try to process it.
            var readerSettings = new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null,
            };

            var doc = new XmlDocument { XmlResolver = null };
            try
            {
                using var textStream = new MemoryStream(content);
                using var xmlReader = XmlReader.Create(textStream, readerSettings);
                doc.Load(xmlReader);
            }
            catch (Exception ex) when (ex is XmlException or InvalidOperationException)
            {
                throw new ArgumentException("This SVG file couldn't be read as valid XML and was rejected.");
            }

            if (doc.DocumentElement == null || !string.Equals(doc.DocumentElement.LocalName, "svg", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("This file isn't a valid SVG image.");

            StripDangerousContent(doc.DocumentElement);

            using var outStream = new MemoryStream();
            using (var writer = XmlWriter.Create(outStream, new XmlWriterSettings { Encoding = Encoding.UTF8 }))
            {
                doc.WriteTo(writer);
            }
            return outStream.ToArray();
        }

        private static void StripDangerousContent(XmlElement element)
        {
            if (element.HasAttributes)
            {
                var attributesToRemove = new List<XmlAttribute>();
                foreach (XmlAttribute attribute in element.Attributes)
                {
                    // Event handler attributes (onload, onclick, onerror, ...) are the main way an SVG
                    // executes script without a <script> element at all -- an onload on the root <svg>
                    // fires the moment the document is opened/navigated to, no user interaction needed.
                    var isEventHandler = attribute.Name.StartsWith("on", StringComparison.OrdinalIgnoreCase);
                    // href/xlink:href with a javascript: URI is the other classic vector (e.g. on an
                    // <a> wrapping part of the image).
                    var isJsUri = (attribute.LocalName.Equals("href", StringComparison.OrdinalIgnoreCase))
                                  && attribute.Value.TrimStart().StartsWith("javascript:", StringComparison.OrdinalIgnoreCase);

                    if (isEventHandler || isJsUri) attributesToRemove.Add(attribute);
                }
                foreach (var attribute in attributesToRemove) element.Attributes.Remove(attribute);
            }

            if (!element.HasChildNodes) return;

            var childrenToRemove = new List<XmlNode>();
            foreach (XmlNode child in element.ChildNodes)
            {
                if (child.NodeType == XmlNodeType.Element)
                {
                    var childElement = (XmlElement)child;
                    if (SvgDangerousElements.Contains(childElement.LocalName))
                    {
                        childrenToRemove.Add(child);
                        continue;
                    }
                    StripDangerousContent(childElement);
                }
                else if (child.NodeType is XmlNodeType.Comment or XmlNodeType.ProcessingInstruction)
                {
                    // Not a script-execution vector by themselves, but there's no legitimate reason a
                    // logo/diagram SVG needs either, and it costs nothing to drop them.
                    childrenToRemove.Add(child);
                }
            }
            foreach (var child in childrenToRemove) element.RemoveChild(child);
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

            // Read fully into memory -- bounded by maxSizeBytes above (15 MB at most across every
            // caller today), and needed regardless to check magic bytes / sanitize SVG content before
            // any of it is trusted enough to store.
            using var buffer = new MemoryStream();
            await stream.CopyToAsync(buffer);
            var content = buffer.ToArray();

            if (ext == ".svg")
            {
                // No fixed magic-byte signature exists for SVG (it's plain XML text), so a spoofed
                // extension isn't the concern here -- embedded script content is. See SanitizeSvg's own
                // comment for exactly what's stripped and why. The sanitized bytes (not the originals)
                // are what actually gets uploaded below.
                content = SanitizeSvg(content);
            }
            else if (!HasValidMagicBytes(ext, content))
            {
                throw new ArgumentException($"This file's content doesn't look like a valid {ext} file.");
            }

            // Never trust the original filename -- generate our own to avoid path traversal / collisions.
            var fileName = $"{Guid.NewGuid()}{ext}";
            var blobName = $"{subfolder}/{fileName}";

            await using var uploadStream = new MemoryStream(content);
            await _blobService.UploadAsync(blobName, uploadStream, GetContentType(ext));

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
