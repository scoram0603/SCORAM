using System.Text.Json;

namespace ScoramAPI.Services
{
    // Single server-side list of the landing-page media slots an admin may replace, plus the validation for the
    // editable Smart Progress section. Pure and database-free so it is easy to unit test.
    //
    // The website keeps the matching list of BUNDLED DEFAULT assets (src/config/landingSlots.js). The server never
    // stores or serves defaults: "no row" simply means "the website shows its own bundled default".
    public sealed record LandingMediaSlot(string Key, string Label, string Group, string Recommended, string Description, bool HasBundledDefault);

    public sealed class SmartProgressContent
    {
        public bool Published { get; set; } = true;
        public string Heading { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public List<string> Highlights { get; set; } = new();
        public string CtaText { get; set; } = string.Empty;
        public string CtaLink { get; set; } = string.Empty;
    }

    public static class LandingContentCatalog
    {
        public const string MediaFolder = "landing";
        public const string MediaUrlPrefix = "/uploads/landing/";
        public const string SmartProgressKey = "smart-progress";
        public const long MaxImageBytes = 3 * 1024 * 1024; // 3 MB -- landing images should be web-sized
        public static readonly string[] AllowedExtensions = { ".png", ".jpg", ".jpeg", ".webp" };

        public const int MaxHeading = 120;
        public const int MaxDescription = 500;
        public const int MaxHighlights = 6;
        public const int MaxHighlight = 120;
        public const int MaxCtaText = 40;
        public const int MaxCtaLink = 300;

        public static readonly IReadOnlyList<LandingMediaSlot> Slots = new List<LandingMediaSlot>
        {
            new("logo", "SCORAM logo (horizontal)", "Brand", "Wide logo on a transparent background, about 600x160 px (PNG/WebP)", "Landing-page footer / header logo.", true),
            new("hero-preview", "Hero app preview", "Feature images", "Portrait phone/app screenshot, about 800x1000 px", "Main image beside the headline at the top of the landing page.", true),
            new("pyp-preview", "Previous Year Papers image", "Feature images", "4:3 landscape, about 1200x900 px", "Image in the Previous Year Papers section.", true),
            new("question-bank-preview", "Question Bank image", "Feature images", "4:3 landscape, about 1200x900 px", "Image in the Question Bank section.", true),
            new("solutions-preview", "Multiple Solutions image", "Feature images", "4:3 landscape, about 1200x900 px", "Image in the Multiple Solving Methods section.", true),
            new("community-preview", "Community image", "Feature images", "4:3 landscape, about 1200x900 px", "Image in the Community section.", true),
            new("app-promo-preview", "Mobile app promotion image", "Feature images", "Portrait phone mockup, about 800x1000 px", "Image in the Get the App section.", true),
            new("smart-progress-image", "Smart Progress image", "Smart Progress", "Real app screenshot, 4:3 landscape, about 1200x900 px", "Optional. The Smart Progress section works without it.", false),
            new("founder-sarvoch", "Founder photo - Sarvoch Priyadarshi", "Founders", "Square-ish portrait, about 600x600 px, face in the upper third", "Shown only for Sarvoch Priyadarshi (landing + Our Team page).", true),
            new("founder-durgesh", "Founder photo - Durgesh Maurya", "Founders", "Square-ish portrait, about 600x600 px, face in the upper third", "Shown only for Durgesh Maurya (landing + Our Team page).", true),
        };

        private static readonly Dictionary<string, LandingMediaSlot> ByKey =
            Slots.ToDictionary(s => s.Key, StringComparer.Ordinal);

        public static bool TryGetSlot(string? key, out LandingMediaSlot slot)
        {
            slot = default!;
            return key != null && ByKey.TryGetValue(key, out slot!);
        }

        public static string MediaRowKey(string slotKey) => $"media:{slotKey}";

        /// <summary>True only for a URL this feature itself produced -- never an arbitrary or external URL.</summary>
        public static bool IsOwnMediaUrl(string? url) =>
            !string.IsNullOrEmpty(url) && url.StartsWith(MediaUrlPrefix, StringComparison.Ordinal)
            && !url.Contains("..") && url.IndexOf('/', MediaUrlPrefix.Length) < 0;

        /// <summary>Extension check for an uploaded file name (case-insensitive). Content (magic bytes) is
        /// verified separately by FileStorageService.</summary>
        public static bool HasAllowedExtension(string? fileName)
        {
            var ext = Path.GetExtension(fileName ?? string.Empty).ToLowerInvariant();
            return AllowedExtensions.Contains(ext);
        }

        /// <summary>Validates and normalises the Smart Progress section. Returns the cleaned content or an error.</summary>
        public static (SmartProgressContent? Content, string? Error) ValidateSmartProgress(SmartProgressContent? input)
        {
            if (input == null) return (null, "No content was provided.");

            var heading = (input.Heading ?? string.Empty).Trim();
            var description = (input.Description ?? string.Empty).Trim();
            var ctaText = (input.CtaText ?? string.Empty).Trim();
            var ctaLink = (input.CtaLink ?? string.Empty).Trim();

            if (heading.Length == 0) return (null, "The heading can't be empty.");
            if (heading.Length > MaxHeading) return (null, $"The heading can be at most {MaxHeading} characters.");
            if (description.Length == 0) return (null, "The description can't be empty.");
            if (description.Length > MaxDescription) return (null, $"The description can be at most {MaxDescription} characters.");

            var highlights = (input.Highlights ?? new List<string>())
                .Select(h => (h ?? string.Empty).Trim()).Where(h => h.Length > 0).ToList();
            if (highlights.Count > MaxHighlights) return (null, $"Use at most {MaxHighlights} highlights.");
            if (highlights.Any(h => h.Length > MaxHighlight)) return (null, $"Each highlight can be at most {MaxHighlight} characters.");

            if (ctaText.Length > MaxCtaText) return (null, $"The button text can be at most {MaxCtaText} characters.");
            if (ctaLink.Length > MaxCtaLink) return (null, "The button link is too long.");
            if ((ctaText.Length == 0) != (ctaLink.Length == 0))
                return (null, "Provide both the button text and the button link, or leave both empty.");
            if (ctaLink.Length > 0 && !IsSafeLink(ctaLink))
                return (null, "The button link must be a path on this site (e.g. /login?mode=register) or an https:// address.");

            return (new SmartProgressContent
            {
                Published = input.Published,
                Heading = heading,
                Description = description,
                Highlights = highlights,
                CtaText = ctaText,
                CtaLink = ctaLink
            }, null);
        }

        /// <summary>Relative site path ("/x", not "//x" and no scheme) or an https URL. Blocks javascript:, data:, etc.</summary>
        public static bool IsSafeLink(string link)
        {
            if (string.IsNullOrWhiteSpace(link) || link.Any(char.IsControl) || link.Contains(' ')) return false;
            if (link.StartsWith("/") && !link.StartsWith("//") && !link.Contains('\\')) return true;
            return Uri.TryCreate(link, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps;
        }

        private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

        public static string Serialize(SmartProgressContent content) => JsonSerializer.Serialize(content, Json);

        /// <summary>Null when the stored JSON is missing or unreadable (the site then uses its safe default copy).</summary>
        public static SmartProgressContent? TryDeserialize(string? json)
        {
            if (string.IsNullOrWhiteSpace(json)) return null;
            try
            {
                var parsed = JsonSerializer.Deserialize<SmartProgressContent>(json, Json);
                if (parsed == null) return null;
                var (clean, error) = ValidateSmartProgress(parsed);
                return error == null ? clean : null;
            }
            catch (JsonException)
            {
                return null;
            }
        }
    }
}
