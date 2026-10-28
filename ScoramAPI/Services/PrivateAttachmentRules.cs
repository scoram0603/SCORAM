using System.Text.RegularExpressions;

namespace ScoramAPI.Services
{
    /// <summary>
    /// The single place that decides which "/uploads/{folder}/..." files are PUBLIC and which belong to a
    /// private conversation, and how a stored chat/DM attachment URL maps to a blob.
    ///
    /// Policy (approved): chat and direct-message attachments are private to the conversation's
    /// participants and are only delivered through the authenticated, membership-checked endpoints
    /// (GET /api/chat/messages/{id}/attachment and GET /api/directmessages/messages/{id}/attachment).
    /// Every other upload folder that appears below is an intentionally public asset. A folder that is in
    /// NEITHER list is NOT public: anything new must be added to <see cref="PublicFolders"/> on purpose.
    /// </summary>
    public static class PrivateAttachmentRules
    {
        public const string ChatFolder = "chat-attachments";
        public const string DirectMessageFolder = "dm-attachments";

        /// <summary>Folders served anonymously by "/uploads/{folder}/{file}": images shown on public pages
        /// and on other students' screens (question and stimulus images, avatars, exam and organization
        /// logos, group-chat room icons, landing-page media). Matched case-insensitively.</summary>
        public static readonly IReadOnlySet<string> PublicFolders = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "question-images",
            "stimulus-images",
            "avatars",
            "exam-logos",
            "organization-logos",
            "chat-room-icons",
            "landing",
        };

        private static readonly IReadOnlySet<string> PrivateFolders = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ChatFolder,
            DirectMessageFolder,
        };

        // Extensions FileStorageService accepts for chat (images + documents) and DM (+ audio) uploads.
        private static readonly Dictionary<string, string> ContentTypeByExtension = new(StringComparer.OrdinalIgnoreCase)
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

        private static readonly HashSet<string> DocumentExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".pdf", ".doc", ".docx", ".xls", ".xlsx", ".ppt", ".pptx",
        };

        // "{guid}.{ext}" -- the only file-name shape FileStorageService ever generates.
        private static readonly Regex FileNamePattern = new(@"^[A-Za-z0-9\-]{1,64}\.[A-Za-z0-9]{1,8}$", RegexOptions.Compiled);

        public static bool IsPrivateFolder(string? folder) => folder != null && PrivateFolders.Contains(folder);

        public static bool IsPublicFolder(string? folder) => folder != null && PublicFolders.Contains(folder);

        /// <summary>The API path clients use to fetch a group-chat message's attachment (needs a Bearer token).</summary>
        public static string ChatAttachmentPath(Guid messageId) => $"/api/chat/messages/{messageId}/attachment";

        /// <summary>The API path clients use to fetch a direct message's attachment (needs a Bearer token).</summary>
        public static string DirectMessageAttachmentPath(Guid messageId) => $"/api/directmessages/messages/{messageId}/attachment";

        public static bool IsDocumentExtension(string extension) => DocumentExtensions.Contains(extension);

        public static string ContentTypeFor(string extension) =>
            ContentTypeByExtension.TryGetValue(extension, out var contentType) ? contentType : "application/octet-stream";

        /// <summary>
        /// Maps a STORED attachment URL to its blob name, but only if it is exactly a file directly inside
        /// <paramref name="requiredFolder"/>. Accepts the relative form FileStorageService writes
        /// ("/uploads/chat-attachments/{guid}.png") and, defensively, an absolute http(s) URL whose path has
        /// that shape (query/fragment ignored). Anything else -- another folder (a public asset!), nested or
        /// traversal paths, odd characters, an extension we never store -- is rejected, so nothing derived
        /// from a stored value can ever read or delete a blob outside the private attachment folders.
        /// </summary>
        public static bool TryGetBlobName(string? storedUrl, string requiredFolder, out string blobName)
        {
            blobName = string.Empty;
            if (string.IsNullOrWhiteSpace(storedUrl) || !IsPrivateFolder(requiredFolder)) return false;

            var path = storedUrl.Trim();
            if (path.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || path.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                if (!Uri.TryCreate(path, UriKind.Absolute, out var uri)) return false;
                path = uri.AbsolutePath;
            }
            else
            {
                var cut = path.IndexOfAny(new[] { '?', '#' });
                if (cut >= 0) path = path[..cut];
            }

            var prefix = $"/uploads/{requiredFolder}/";
            if (!path.StartsWith(prefix, StringComparison.Ordinal)) return false;

            var fileName = path[prefix.Length..];
            if (!FileNamePattern.IsMatch(fileName)) return false;
            if (!ContentTypeByExtension.ContainsKey(Path.GetExtension(fileName))) return false;

            blobName = $"{requiredFolder}/{fileName}";
            return true;
        }
    }
}
