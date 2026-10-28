using ScoramAPI.Services;

namespace ScoramAPI.Middleware
{
    /// <summary>
    /// Runs BEFORE app.UseStaticFiles(). Any request under "/uploads/" whose first folder is not one of the
    /// explicitly public asset folders (<see cref="PrivateAttachmentRules.PublicFolders"/>) is answered with
    /// a bare 404 right here. That covers the private chat/DM attachment folders and any unclassified
    /// folder, both for the Azure-backed UploadedFilesController and for files that may still sit in
    /// wwwroot/uploads on a developer machine or an old deployment -- which UseStaticFiles would otherwise
    /// serve to anyone. It reads nothing but the path and never touches storage.
    /// </summary>
    public class UploadsPrivacyMiddleware
    {
        private readonly RequestDelegate _next;

        public UploadsPrivacyMiddleware(RequestDelegate next) => _next = next;

        public async Task InvokeAsync(HttpContext context)
        {
            if (IsBlocked(context.Request.Path))
            {
                context.Response.StatusCode = StatusCodes.Status404NotFound;
                return;
            }

            await _next(context);
        }

        /// <summary>
        /// True for "/uploads/..." paths that must not be served anonymously. The path is NORMALISED first, the
        /// way the file system (and therefore UseStaticFiles) will read it, so none of these slip past:
        /// repeated or leading slashes ("//uploads/chat-attachments/x.png" -- confirmed to reach a wwwroot file on
        /// Kestrel with the earlier prefix-only check), backslashes, "." / ".." segments, and trailing dots or
        /// spaces on a segment (Windows ignores them). After that the rule is an exact allow-list on the folder
        /// name: anything that is not a known public folder is blocked.
        /// </summary>
        public static bool IsBlocked(PathString path)
        {
            var segments = new List<string>();
            var oddDots = false; // a segment such as ".. " or "a..b": not a plain ".." but not trustworthy either
            foreach (var raw in (path.Value ?? string.Empty).Split('/', '\\'))
            {
                var segment = raw.TrimEnd('.', ' ');
                if (raw == "..")
                {
                    if (segments.Count > 0) segments.RemoveAt(segments.Count - 1);
                    continue;
                }
                if (raw != "." && raw.Contains("..")) oddDots = true;
                if (raw == "." || segment.Length == 0) continue; // "." and empty segments (double slashes) vanish
                segments.Add(segment);
            }

            if (segments.Count == 0 || !string.Equals(segments[0], "uploads", StringComparison.OrdinalIgnoreCase))
                return false;

            // "/uploads" on its own, or "/uploads/{folder}/...": only an exact public folder name is allowed through.
            return oddDots || segments.Count < 2 || !PrivateAttachmentRules.IsPublicFolder(segments[1]);
        }
    }
}
