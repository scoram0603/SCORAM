using System.IO.Compression;
using System.Text;

namespace ScoramAPI.Services
{
    // Extracts and validates a SCORAM-BULK-UPLOAD.zip package (see spec section 51: questions.json +
    // images/ + optional metadata.json) into memory. Deliberately independent of PYP vs PYQ -- both
    // bulk-import flows share this. Doesn't touch the database, EF Core, or IFileStorageService
    // itself; it hands back plain bytes and lets each caller (BulkImportService /
    // QuestionBankImportService) decide how to turn "row says questionImage: q001.png" into an
    // actual staged blob.
    public interface IBulkUploadZipService
    {
        /// <summary>Reads a ZIP stream, validates it (path traversal, zip-bomb / entry-count /
        /// total-size limits, allowed image extensions), and returns its questions.json text plus
        /// every file found under images/ keyed by filename (case-insensitive -- "Q001.PNG" and
        /// "q001.png" are the same key). Throws InvalidDataException with a human-readable message
        /// on any structural problem -- missing questions.json, disallowed entry, oversized archive,
        /// etc. -- which callers should turn into a 400, same convention as
        /// IBulkImportService.ParseAsync.</summary>
        BulkUploadZipContents Extract(Stream zipStream);
    }

    public class BulkUploadZipContents
    {
        public string QuestionsJson { get; set; } = string.Empty;

        // Present only if the ZIP included a metadata.json at its root (spec section 18) -- neither
        // import service currently reads anything out of this (the PYP flow's "which paper" and the
        // PYQ flow's "default language" are both already supplied as separate request fields, same
        // as for a plain JSON/Excel upload), but it's captured here in case a future admin-facing
        // need for it comes up, rather than silently discarding it during extraction.
        public string? MetadataJson { get; set; }

        // Filename (as it appeared under images/, case-insensitive) -> raw bytes. Kept as byte[]
        // rather than re-opened ZipArchiveEntry streams since the ZipArchive itself is disposed
        // before these bytes are used (staging happens after Extract returns).
        public Dictionary<string, byte[]> Images { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    }

    public class BulkUploadZipService : IBulkUploadZipService
    {
        // Generous bounds for "a few thousand questions, most text-only, some with one or two images
        // each" (spec section 48: "test with 1000+ questions") without leaving the endpoint open to
        // a zip-bomb (a tiny compressed file that expands to gigabytes) or an archive stuffed with
        // thousands of tiny junk entries. Per-image size is separately enforced by
        // IFileStorageService.SaveImageFromStreamAsync's own 5 MB cap -- these are just the
        // ZIP-level outer bounds. If a real upload needs more than this, raise both this constant and
        // the calling endpoint's [RequestSizeLimit] together.
        private const long MaxTotalUncompressedBytes = 300L * 1024 * 1024; // 300 MB
        private const int MaxEntries = 5000;
        private static readonly string[] AllowedImageExtensions = { ".png", ".jpg", ".jpeg", ".webp", ".svg" };

        public BulkUploadZipContents Extract(Stream zipStream)
        {
            using var archive = new ZipArchive(zipStream, ZipArchiveMode.Read, leaveOpen: true);

            if (archive.Entries.Count > MaxEntries)
                throw new InvalidDataException($"This ZIP has too many entries (max {MaxEntries}).");

            string? questionsJson = null;
            string? metadataJson = null;
            var images = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
            long totalUncompressed = 0;

            foreach (var entry in archive.Entries)
            {
                // Directory entries show up with an empty Name (and a trailing '/' in FullName) --
                // nothing to read, just skip them.
                if (string.IsNullOrEmpty(entry.Name)) continue;

                var normalizedPath = entry.FullName.Replace('\\', '/');

                // Reject path traversal and absolute paths outright -- never trust an entry name.
                // Nothing here extracts to a real filesystem path built from entry.FullName (bytes
                // are read straight into memory below), so there's no literal traversal to exploit,
                // but refusing these outright is a clear, defensible line regardless of how the
                // bytes end up being used.
                if (normalizedPath.Contains("..") || normalizedPath.StartsWith('/'))
                    throw new InvalidDataException($"Unsafe entry name in ZIP: \"{entry.FullName}\".");

                // entry.Length is the entry's UNCOMPRESSED size -- exactly the number a zip-bomb
                // check needs (entry.CompressedLength would let a small file claim to expand to
                // anything).
                totalUncompressed += entry.Length;
                if (totalUncompressed > MaxTotalUncompressedBytes)
                    throw new InvalidDataException(
                        $"This ZIP is too large once decompressed (max {MaxTotalUncompressedBytes / (1024 * 1024)} MB total).");

                if (normalizedPath.Equals("questions.json", StringComparison.OrdinalIgnoreCase))
                {
                    questionsJson = ReadEntryAsString(entry);
                }
                else if (normalizedPath.Equals("metadata.json", StringComparison.OrdinalIgnoreCase))
                {
                    metadataJson = ReadEntryAsString(entry);
                }
                else if (normalizedPath.StartsWith("images/", StringComparison.OrdinalIgnoreCase))
                {
                    var fileName = Path.GetFileName(normalizedPath);
                    if (string.IsNullOrEmpty(fileName)) continue; // a sub-folder under images/, not a file

                    var ext = Path.GetExtension(fileName).ToLowerInvariant();
                    if (!AllowedImageExtensions.Contains(ext))
                        throw new InvalidDataException(
                            $"\"{fileName}\" isn't an allowed image type ({string.Join(", ", AllowedImageExtensions)}).");

                    images[fileName] = ReadEntryAsBytes(entry);
                }
                // Anything else (a stray README, a .DS_Store, an executable slipped in under some
                // other top-level name) is silently ignored rather than rejected outright -- refusing
                // the whole ZIP over an incidental extra file would be an unhelpfully harsh failure
                // mode for something an admin zipped up by hand, and nothing outside questions.json /
                // metadata.json / images/ is ever read or executed.
            }

            if (questionsJson == null)
                throw new InvalidDataException("The ZIP must contain a questions.json file at its root.");

            return new BulkUploadZipContents
            {
                QuestionsJson = questionsJson,
                MetadataJson = metadataJson,
                Images = images
            };
        }

        private static string ReadEntryAsString(ZipArchiveEntry entry)
        {
            using var stream = entry.Open();
            using var reader = new StreamReader(stream, Encoding.UTF8);
            return reader.ReadToEnd();
        }

        private static byte[] ReadEntryAsBytes(ZipArchiveEntry entry)
        {
            using var stream = entry.Open();
            using var ms = new MemoryStream();
            stream.CopyTo(ms);
            return ms.ToArray();
        }
    }
}
