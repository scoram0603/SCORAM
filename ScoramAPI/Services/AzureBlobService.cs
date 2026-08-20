using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using ScoramAPI.Enums;

namespace ScoramAPI.Services
{
    // AZURE BLOB STORAGE -- thin, controller-agnostic wrapper around the Azure.Storage.Blobs SDK.
    // Nothing in here touches the database (that's DocumentsController + the Document entity) and
    // nothing in here is aware of HTTP (that's DocumentsController too) -- this only knows how to
    // move bytes into/out of/around the private "uploads" container. Keeping the SDK calls isolated
    // here (rather than scattered across controllers, per Step 4) means the Azure SDK is the only
    // thing that would need to change if SCORAM ever swaps storage providers.
    public interface IAzureBlobService
    {
        /// <summary>Builds a unique, collision-proof blob name for a new upload, e.g.
        /// "question-images/550e8400-e29b-41d4-a716-446655440000.jpg". Never derived from the
        /// uploader's original filename (Step 5) -- that's preserved separately in Document.FileName.</summary>
        string BuildBlobName(DocumentCategory category, string fileExtension);

        /// <summary>Uploads a new blob. Fails (throws) if a blob with this exact name already exists
        /// rather than silently overwriting -- shouldn't happen given BuildBlobName's Guid uniqueness,
        /// but this is deliberate: a caller wanting to REPLACE a file should delete the old blob
        /// itself once the new one is confirmed uploaded (see Step 14), not rely on silent overwrite.</summary>
        Task UploadAsync(string blobName, Stream content, string contentType, CancellationToken cancellationToken = default);

        /// <summary>Opens a readable stream over the blob's content for the controller to pipe into a
        /// FileStreamResult. Returns null if the blob doesn't exist (e.g. the SQL row survived but the
        /// blob was somehow removed out-of-band) so the controller can turn that into a clean 404
        /// instead of a raw Azure exception reaching the client.</summary>
        Task<Stream?> DownloadAsync(string blobName, CancellationToken cancellationToken = default);

        /// <summary>Best-effort existence check, e.g. before trusting a blob name that came from
        /// somewhere other than a fresh upload.</summary>
        Task<bool> ExistsAsync(string blobName, CancellationToken cancellationToken = default);

        /// <summary>Deletes a blob. Returns true if it was actually deleted, false if it was already
        /// gone (not an error -- deleting something twice is a no-op, not a failure). Throws only on a
        /// genuine storage-layer failure (auth, network, ...), which the caller should treat as "do
        /// not delete the SQL metadata row yet" (Step 13).</summary>
        Task<bool> DeleteAsync(string blobName, CancellationToken cancellationToken = default);
    }

    public class AzureBlobService : IAzureBlobService
    {
        // Step 5's exact virtual-folder names. DocumentCategory is the source of truth for what
        // categories exist; this is just where each one lives inside the container.
        private static readonly Dictionary<DocumentCategory, string> FolderByCategory = new()
        {
            [DocumentCategory.QuestionImage] = "question-images",
            [DocumentCategory.PyqPdf] = "pyq-pdfs",
            [DocumentCategory.ExamPdf] = "exam-pdfs",
            [DocumentCategory.ProfileImage] = "profile-images",
        };

        private readonly BlobContainerClient _containerClient;
        private readonly ILogger<AzureBlobService> _logger;

        // Constructing a BlobContainerClient from an already-built BlobServiceClient is purely local
        // (no network call), so this is safe to do even while AzureBlobStorage:ConnectionString is
        // still the DEMO placeholder -- nothing actually reaches Azure until UploadAsync/
        // DownloadAsync/ExistsAsync/DeleteAsync is called.
        public AzureBlobService(BlobServiceClient blobServiceClient, IConfiguration config, ILogger<AzureBlobService> logger)
        {
            var containerName = config["AzureBlobStorage:ContainerName"] ?? "uploads";
            _containerClient = blobServiceClient.GetBlobContainerClient(containerName);
            _logger = logger;
        }

        public string BuildBlobName(DocumentCategory category, string fileExtension)
        {
            var folder = FolderByCategory[category];
            var ext = fileExtension.StartsWith('.') ? fileExtension : $".{fileExtension}";
            return $"{folder}/{Guid.NewGuid()}{ext.ToLowerInvariant()}";
        }

        public async Task UploadAsync(string blobName, Stream content, string contentType, CancellationToken cancellationToken = default)
        {
            _logger.LogInformation("Blob upload started for {BlobName}", blobName);

            var blobClient = _containerClient.GetBlobClient(blobName);
            var options = new BlobUploadOptions
            {
                HttpHeaders = new BlobHttpHeaders { ContentType = contentType }
            };

            await blobClient.UploadAsync(content, options, cancellationToken);

            _logger.LogInformation("Blob upload completed for {BlobName}", blobName);
        }

        public async Task<Stream?> DownloadAsync(string blobName, CancellationToken cancellationToken = default)
        {
            var blobClient = _containerClient.GetBlobClient(blobName);

            try
            {
                var result = await blobClient.DownloadStreamingAsync(cancellationToken: cancellationToken);
                return result.Value.Content;
            }
            catch (RequestFailedException ex) when (ex.Status == 404)
            {
                _logger.LogWarning("Blob not found in storage: {BlobName}", blobName);
                return null;
            }
        }

        public async Task<bool> ExistsAsync(string blobName, CancellationToken cancellationToken = default)
        {
            var blobClient = _containerClient.GetBlobClient(blobName);
            var response = await blobClient.ExistsAsync(cancellationToken);
            return response.Value;
        }

        public async Task<bool> DeleteAsync(string blobName, CancellationToken cancellationToken = default)
        {
            var blobClient = _containerClient.GetBlobClient(blobName);
            var response = await blobClient.DeleteIfExistsAsync(cancellationToken: cancellationToken);

            _logger.LogInformation("Blob deletion completed for {BlobName} (existed: {Existed})", blobName, response.Value);

            return response.Value;
        }
    }
}
