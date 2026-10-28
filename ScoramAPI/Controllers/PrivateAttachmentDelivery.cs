using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using ScoramAPI.Services;

namespace ScoramAPI.Controllers
{
    /// <summary>Outcome of <see cref="PrivateAttachmentDelivery.CleanupAsync"/>.</summary>
    public enum AttachmentCleanupResult
    {
        /// <summary>The message had no attachment.</summary>
        NothingToDelete,
        /// <summary>The blob was deleted, or was already gone (a repeat delete is not an error).</summary>
        Deleted,
        /// <summary>The stored value is not a valid private-attachment path, so NOTHING was touched
        /// (this is what protects public assets and arbitrary paths from ever being deleted here).</summary>
        SkippedInvalid,
        /// <summary>Storage failed. The caller keeps the stored URL so a later retry can try again.</summary>
        Failed,
    }

    /// <summary>
    /// Shared by ChatController and DirectMessagesController so the two private-attachment flows behave
    /// identically: how a (membership-checked) attachment is streamed back, and how its blob is removed
    /// when the message is deleted.
    /// </summary>
    public static class PrivateAttachmentDelivery
    {
        /// <summary>
        /// Streams the blob behind a stored attachment URL. The CALLER must already have loaded the message
        /// and verified the requester may see it -- this method only turns a stored value into a response,
        /// and never accepts a blob name from the client. Anything wrong (value not a valid path in
        /// <paramref name="folder"/>, blob missing) is a bare 404; a storage failure is a bare 502 with no
        /// detail. Responses are never cacheable by shared caches.
        /// </summary>
        public static async Task<IActionResult> StreamAsync(
            ControllerBase controller,
            IAzureBlobService blobs,
            string? storedUrl,
            string folder,
            CancellationToken cancellationToken)
        {
            if (!PrivateAttachmentRules.TryGetBlobName(storedUrl, folder, out var blobName))
                return controller.NotFound();

            Stream? stream;
            try
            {
                stream = await blobs.DownloadAsync(blobName, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw; // the client went away; let ASP.NET treat it as an aborted request
            }
            catch
            {
                return controller.StatusCode(StatusCodes.Status502BadGateway);
            }

            if (stream == null) return controller.NotFound();

            var extension = Path.GetExtension(blobName).ToLowerInvariant();
            var headers = controller.Response.Headers;
            headers.CacheControl = "private, no-store";
            headers.Pragma = "no-cache";
            headers.Vary = "Authorization";
            headers["X-Content-Type-Options"] = "nosniff";

            // Documents are handed to the browser/app as downloads; images and voice notes render inline.
            // The file name is a fixed neutral one -- the stored blob name is never revealed.
            var disposition = PrivateAttachmentRules.IsDocumentExtension(extension) ? "attachment" : "inline";
            headers.ContentDisposition = $"{disposition}; filename=\"attachment{extension}\"";

            if (extension == ".svg")
            {
                // Same defence-in-depth as UploadedFilesController: no script even if opened directly.
                headers["Content-Security-Policy"] = "script-src 'none'; sandbox;";
            }

            return controller.File(stream, PrivateAttachmentRules.ContentTypeFor(extension));
        }

        /// <summary>
        /// Deletes the blob behind a message's stored attachment URL, after validating that the value is a
        /// file directly inside <paramref name="folder"/> (a private attachment folder). Idempotent: a blob
        /// that is already gone counts as deleted. Never throws for storage problems -- it reports
        /// <see cref="AttachmentCleanupResult.Failed"/> so the caller can keep the URL and let a retry
        /// finish the job.
        /// </summary>
        public static async Task<AttachmentCleanupResult> CleanupAsync(
            IAzureBlobService blobs,
            string? storedUrl,
            string folder,
            ILogger? logger,
            Guid messageId,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(storedUrl)) return AttachmentCleanupResult.NothingToDelete;

            if (!PrivateAttachmentRules.TryGetBlobName(storedUrl, folder, out var blobName))
            {
                logger?.LogWarning("Message {MessageId} has an attachment value that is not a valid private path; no blob was touched.", messageId);
                return AttachmentCleanupResult.SkippedInvalid;
            }

            try
            {
                await blobs.DeleteAsync(blobName, cancellationToken); // false = already gone, which is fine
                return AttachmentCleanupResult.Deleted;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                // Exception type only: never the blob name, credentials or the storage message.
                logger?.LogWarning("Attachment blob delete failed for message {MessageId} ({ExceptionType}); URL kept for retry.", messageId, ex.GetType().Name);
                return AttachmentCleanupResult.Failed;
            }
        }
    }
}
