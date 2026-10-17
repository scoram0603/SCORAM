using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using ScoramAPI.Data;
using ScoramAPI.Models;

namespace ScoramAPI.Services
{
    // ADMIN PLATFORM -- runtime app configuration: maintenance mode, feature flags, and the minimum /
    // recommended app versions behind "force update".
    //
    // Design rules:
    //   * Only keys listed in Definitions can ever be stored. The admin UI is generated from this list and
    //     the server re-validates every value, so a crafted request can't write arbitrary keys or values.
    //   * Every change appends an AppSettingRevision -- that history is the emergency rollback path.
    //   * Reads are served from a 30-second in-memory cache (invalidated immediately on THIS instance when a
    //     setting changes). On a multi-instance deployment another instance can lag by up to 30 seconds.
    //   * Every consumer of these settings fails OPEN: if the database is unreachable the app keeps working
    //     rather than locking every student out because the config table could not be read.

    public enum AppSettingType { Bool, Text, Version, Url }

    public sealed record AppSettingDefinition(
        string Key,
        AppSettingType Type,
        string Default,
        string Group,
        string Label,
        string Description,
        bool SuperAdminOnly,
        int MaxLength = 300);

    public enum AppConfigResultStatus { Ok, Invalid, Forbidden, NotFound }

    public sealed class AppConfigChangeResult
    {
        public AppConfigResultStatus Status { get; init; }
        public string? Error { get; init; }
        public List<string> ChangedKeys { get; init; } = new();
        public Guid BatchId { get; init; }
    }

    public sealed class AppVersionStatus
    {
        // "ok" | "update_recommended" | "update_required"
        public string Status { get; init; } = "ok";
        public string? MinVersion { get; init; }
        public string? RecommendedVersion { get; init; }
        public string? UpdateUrl { get; init; }
        public string? ReleaseNotes { get; init; }
    }

    public interface IAppConfigService
    {
        IReadOnlyList<AppSettingDefinition> Definitions { get; }

        /// <summary>Every known key with its effective value (stored value, else the default).</summary>
        Task<IReadOnlyDictionary<string, string>> GetAllAsync(CancellationToken ct = default);

        Task<bool> GetBoolAsync(string key, CancellationToken ct = default);

        Task<AppConfigChangeResult> UpdateAsync(
            IReadOnlyDictionary<string, string> changes, Guid adminId, bool isSuperAdmin, string? note, CancellationToken ct = default);

        Task<AppConfigChangeResult> RollbackAsync(Guid revisionId, Guid adminId, bool isSuperAdmin, CancellationToken ct = default);

        Task<AppVersionStatus> EvaluateVersionAsync(string platform, string? clientVersion, CancellationToken ct = default);

        /// <summary>Compares dotted numeric versions ("1.2.3", optional "+build" suffix ignored). Null when
        /// either side is not a valid version.</summary>
        int? CompareVersions(string? a, string? b);
    }

    public class AppConfigService : IAppConfigService
    {
        public const string MaintenanceEnabled = "maintenance.enabled";
        public const string MaintenanceMessage = "maintenance.message";
        public const string FeatureRegistration = "feature.registration";
        public const string FeatureFeedback = "feature.feedback";
        public const string ReleaseNotes = "app.releaseNotes";

        private const string CacheKey = "appconfig:all:v1";
        private static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(30);

        private static readonly Regex VersionPattern = new(@"^\d{1,9}(\.\d{1,9}){0,3}$", RegexOptions.Compiled);

        private static readonly AppSettingDefinition[] Defs =
        {
            new(MaintenanceEnabled, AppSettingType.Bool, "false", "Maintenance", "Maintenance mode",
                "While on, the student API answers 503 with the message below. Admin panel and health checks keep working.", true),
            new(MaintenanceMessage, AppSettingType.Text, "SCORAM is undergoing scheduled maintenance. Please try again shortly.",
                "Maintenance", "Maintenance message", "Shown to students while maintenance mode is on.", false, 300),

            new(FeatureRegistration, AppSettingType.Bool, "true", "Feature flags", "New student registration",
                "Turn off to stop new sign-ups (existing students are unaffected). Enforced by the server.", true),
            new(FeatureFeedback, AppSettingType.Bool, "true", "Feature flags", "Student feedback submissions",
                "Turn off to stop accepting new feedback from the student apps. Enforced by the server.", false),

            new("app.android.minVersion", AppSettingType.Version, "", "App versions", "Android - minimum supported version",
                "Android builds below this are forced to update before continuing. Leave empty for no minimum.", true, 20),
            new("app.android.recommendedVersion", AppSettingType.Version, "", "App versions", "Android - recommended version",
                "Android builds below this see a dismissible update prompt.", false, 20),
            new("app.android.updateUrl", AppSettingType.Url, "", "App versions", "Android - update link",
                "https:// link to the Play Store listing. Required while a minimum version is set.", false, 500),

            new("app.ios.minVersion", AppSettingType.Version, "", "App versions", "iOS - minimum supported version",
                "iOS builds below this are forced to update before continuing. Leave empty for no minimum.", true, 20),
            new("app.ios.recommendedVersion", AppSettingType.Version, "", "App versions", "iOS - recommended version",
                "iOS builds below this see a dismissible update prompt.", false, 20),
            new("app.ios.updateUrl", AppSettingType.Url, "", "App versions", "iOS - update link",
                "https:// link to the App Store listing. Required while a minimum version is set.", false, 500),

            new(ReleaseNotes, AppSettingType.Text, "", "App versions", "Release notes",
                "Short text shown in the update prompt.", false, 1000),
        };

        private static readonly Dictionary<string, AppSettingDefinition> ByKey =
            Defs.ToDictionary(d => d.Key, StringComparer.Ordinal);

        private readonly ScoramDbContext _db;
        private readonly IMemoryCache _cache;
        private readonly ILogger<AppConfigService> _logger;

        public AppConfigService(ScoramDbContext db, IMemoryCache cache, ILogger<AppConfigService> logger)
        {
            _db = db;
            _cache = cache;
            _logger = logger;
        }

        public IReadOnlyList<AppSettingDefinition> Definitions => Defs;

        public async Task<IReadOnlyDictionary<string, string>> GetAllAsync(CancellationToken ct = default)
        {
            if (_cache.TryGetValue(CacheKey, out IReadOnlyDictionary<string, string>? cached) && cached != null)
                return cached;

            var stored = await _db.AppSettings.AsNoTracking().ToListAsync(ct);
            var merged = Defs.ToDictionary(d => d.Key, d => d.Default, StringComparer.Ordinal);
            foreach (var row in stored)
            {
                if (merged.ContainsKey(row.Key)) merged[row.Key] = row.Value;
            }

            _cache.Set(CacheKey, (IReadOnlyDictionary<string, string>)merged, CacheTtl);
            return merged;
        }

        public async Task<bool> GetBoolAsync(string key, CancellationToken ct = default)
        {
            // Fail OPEN: a config read problem must never be the reason registration / feedback /
            // the whole student API stops working.
            try
            {
                var all = await GetAllAsync(ct);
                if (!ByKey.TryGetValue(key, out var def)) return false;
                var raw = all.TryGetValue(key, out var v) ? v : def.Default;
                return string.Equals(raw, "true", StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not read app setting {Key}; using its default.", key);
                return ByKey.TryGetValue(key, out var d) && string.Equals(d.Default, "true", StringComparison.OrdinalIgnoreCase);
            }
        }

        public int? CompareVersions(string? a, string? b)
        {
            var pa = ParseVersion(a);
            var pb = ParseVersion(b);
            if (pa == null || pb == null) return null;

            var length = Math.Max(pa.Length, pb.Length);
            for (var i = 0; i < length; i++)
            {
                var x = i < pa.Length ? pa[i] : 0;
                var y = i < pb.Length ? pb[i] : 0;
                if (x != y) return x < y ? -1 : 1;
            }
            return 0;
        }

        private static long[]? ParseVersion(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return null;
            var core = raw.Trim();
            var plus = core.IndexOf('+');
            if (plus >= 0) core = core.Substring(0, plus);
            if (!VersionPattern.IsMatch(core)) return null;
            return core.Split('.').Select(long.Parse).ToArray();
        }

        public async Task<AppVersionStatus> EvaluateVersionAsync(string platform, string? clientVersion, CancellationToken ct = default)
        {
            var p = (platform ?? string.Empty).Trim().ToLowerInvariant();
            if (p != "android" && p != "ios") return new AppVersionStatus();

            IReadOnlyDictionary<string, string> all;
            try { all = await GetAllAsync(ct); }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Version check could not read app config; reporting ok (fail open).");
                return new AppVersionStatus();
            }

            string Get(string suffix) => all.TryGetValue($"app.{p}.{suffix}", out var v) ? v : string.Empty;
            var min = Get("minVersion");
            var recommended = Get("recommendedVersion");
            var url = Get("updateUrl");
            var notes = all.TryGetValue(ReleaseNotes, out var rn) ? rn : string.Empty;

            string status = "ok";
            // An unparsable / missing client version is treated as "ok" -- never lock out a client we
            // cannot identify (CompareVersions returns null in that case).
            if (!string.IsNullOrEmpty(min) && CompareVersions(clientVersion, min) is int cmpMin && cmpMin < 0)
                status = "update_required";
            else if (!string.IsNullOrEmpty(recommended) && CompareVersions(clientVersion, recommended) is int cmpRec && cmpRec < 0)
                status = "update_recommended";

            return new AppVersionStatus
            {
                Status = status,
                MinVersion = string.IsNullOrEmpty(min) ? null : min,
                RecommendedVersion = string.IsNullOrEmpty(recommended) ? null : recommended,
                UpdateUrl = string.IsNullOrEmpty(url) ? null : url,
                ReleaseNotes = string.IsNullOrEmpty(notes) ? null : notes
            };
        }

        // ---- validation -------------------------------------------------------------------------------

        private string? ValidateAndNormalize(AppSettingDefinition def, string? raw, out string normalized)
        {
            normalized = (raw ?? string.Empty).Trim();

            if (normalized.Length > def.MaxLength) return $"{def.Label} must be at most {def.MaxLength} characters.";
            if (normalized.Any(char.IsControl)) return $"{def.Label} contains invalid characters.";

            switch (def.Type)
            {
                case AppSettingType.Bool:
                    if (string.Equals(normalized, "true", StringComparison.OrdinalIgnoreCase)) { normalized = "true"; return null; }
                    if (string.Equals(normalized, "false", StringComparison.OrdinalIgnoreCase)) { normalized = "false"; return null; }
                    return $"{def.Label} must be true or false.";

                case AppSettingType.Version:
                    if (normalized.Length == 0) return null;
                    return VersionPattern.IsMatch(normalized) ? null : $"{def.Label} must look like 1.2.3.";

                case AppSettingType.Url:
                    if (normalized.Length == 0) return null;
                    if (!Uri.TryCreate(normalized, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
                        return $"{def.Label} must be a valid https:// link.";
                    return null;

                default:
                    if (def.Key == MaintenanceMessage && normalized.Length == 0)
                        return "The maintenance message cannot be empty.";
                    return null;
            }
        }

        // Rules that involve more than one key, checked against the values as they WOULD be after the change.
        private string? ValidateCombination(IReadOnlyDictionary<string, string> effective)
        {
            foreach (var platform in new[] { "android", "ios" })
            {
                var min = effective[$"app.{platform}.minVersion"];
                var rec = effective[$"app.{platform}.recommendedVersion"];
                var url = effective[$"app.{platform}.updateUrl"];
                var label = platform == "ios" ? "iOS" : "Android";

                if (!string.IsNullOrEmpty(min) && string.IsNullOrEmpty(url))
                    return $"{label}: set an update link before setting a minimum version, otherwise users below it would be blocked with nowhere to go.";

                if (!string.IsNullOrEmpty(min) && !string.IsNullOrEmpty(rec) && CompareVersions(min, rec) is int c && c > 0)
                    return $"{label}: the minimum version cannot be higher than the recommended version.";
            }
            return null;
        }

        public async Task<AppConfigChangeResult> UpdateAsync(
            IReadOnlyDictionary<string, string> changes, Guid adminId, bool isSuperAdmin, string? note, CancellationToken ct = default)
        {
            if (changes.Count == 0)
                return new AppConfigChangeResult { Status = AppConfigResultStatus.Invalid, Error = "No changes were supplied." };

            var current = new Dictionary<string, string>(await GetAllAsync(ct), StringComparer.Ordinal);
            var effective = new Dictionary<string, string>(current, StringComparer.Ordinal);
            var toWrite = new Dictionary<string, string>(StringComparer.Ordinal);

            foreach (var (key, rawValue) in changes)
            {
                if (!ByKey.TryGetValue(key, out var def))
                    return new AppConfigChangeResult { Status = AppConfigResultStatus.Invalid, Error = $"'{key}' is not a known setting." };

                var error = ValidateAndNormalize(def, rawValue, out var normalized);
                if (error != null)
                    return new AppConfigChangeResult { Status = AppConfigResultStatus.Invalid, Error = error };

                if (normalized == current[key]) continue; // no-op, not recorded

                if (def.SuperAdminOnly && !isSuperAdmin)
                    return new AppConfigChangeResult
                    {
                        Status = AppConfigResultStatus.Forbidden,
                        Error = $"Only a Super Admin can change \"{def.Label}\"."
                    };

                toWrite[key] = normalized;
                effective[key] = normalized;
            }

            if (toWrite.Count == 0)
                return new AppConfigChangeResult { Status = AppConfigResultStatus.Ok, ChangedKeys = new List<string>() };

            var comboError = ValidateCombination(effective);
            if (comboError != null)
                return new AppConfigChangeResult { Status = AppConfigResultStatus.Invalid, Error = comboError };

            var batchId = Guid.NewGuid();
            var now = DateTime.UtcNow;
            var trimmedNote = string.IsNullOrWhiteSpace(note) ? null : (note.Trim().Length > 300 ? note.Trim().Substring(0, 300) : note.Trim());

            await using var tx = await _db.Database.BeginTransactionAsync(ct);

            var keys = toWrite.Keys.ToList();
            var existingRows = await _db.AppSettings.Where(s => keys.Contains(s.Key)).ToListAsync(ct);

            foreach (var (key, value) in toWrite)
            {
                var row = existingRows.FirstOrDefault(r => r.Key == key);
                var oldValue = row?.Value;
                if (row == null)
                {
                    _db.AppSettings.Add(new AppSetting { Key = key, Value = value, UpdatedAt = now, UpdatedByAdminId = adminId });
                }
                else
                {
                    row.Value = value;
                    row.UpdatedAt = now;
                    row.UpdatedByAdminId = adminId;
                }

                _db.AppSettingRevisions.Add(new AppSettingRevision
                {
                    Key = key,
                    OldValue = oldValue,
                    NewValue = value,
                    ChangedAt = now,
                    ChangedByAdminId = adminId,
                    BatchId = batchId,
                    Note = trimmedNote
                });
            }

            await _db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);

            _cache.Remove(CacheKey);
            return new AppConfigChangeResult { Status = AppConfigResultStatus.Ok, ChangedKeys = keys, BatchId = batchId };
        }

        public async Task<AppConfigChangeResult> RollbackAsync(Guid revisionId, Guid adminId, bool isSuperAdmin, CancellationToken ct = default)
        {
            var revision = await _db.AppSettingRevisions.AsNoTracking().FirstOrDefaultAsync(r => r.Id == revisionId, ct);
            if (revision == null)
                return new AppConfigChangeResult { Status = AppConfigResultStatus.NotFound, Error = "That revision does not exist." };

            if (!ByKey.TryGetValue(revision.Key, out var def))
                return new AppConfigChangeResult { Status = AppConfigResultStatus.Invalid, Error = "That setting no longer exists." };

            // Restoring "no stored value" means going back to the built-in default.
            var restore = revision.OldValue ?? def.Default;
            return await UpdateAsync(
                new Dictionary<string, string> { [revision.Key] = restore },
                adminId, isSuperAdmin, $"Rollback of revision {revision.Id:D}", ct);
        }
    }
}
