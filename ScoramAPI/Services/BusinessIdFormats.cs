using System.Text;
using System.Text.RegularExpressions;
using ScoramAPI.Models;

namespace ScoramAPI.Services
{
    // Pure, database-free rules for what a Business ID looks like -- kept separate from
    // BusinessIdService so it can be unit-tested without a database, and so the backend validator
    // (SuperAdmin manual change), the generator and the backfill can never disagree on a format.
    //
    //   Exam       EXM + organization code (letters) + sequence (3+ digits)   EXMSSC001
    //   Subject    SUB + sequence (3+ digits)                                 SUB001
    //   Test       TST + sequence (4+ digits)                                 TST0001
    //   Mock Test  MCK + sequence (4+ digits)                                 MCK0001
    //   Admin      ADM + sequence (4+ digits)                                 ADM0001
    //
    // "3+" / "4+" digits: the number is zero-padded to that width and simply grows past it
    // (SUB999 -> SUB1000) rather than ever wrapping or truncating.
    public static class BusinessIdFormats
    {
        public const string ExamPrefix = "EXM";
        public const string SubjectPrefix = "SUB";
        public const string TestPrefix = "TST";
        public const string MockTestPrefix = "MCK";
        public const string AdminPrefix = "ADM";
        public const string SharedStimulusPrefix = "STM";

        // Letters only, on purpose: with digits allowed in the code, "EXMSSC2001" could be code
        // "SSC2"+001 or "SSC"+2001 and the sequence could not be parsed back out unambiguously.
        public const int MaxOrganizationCodeLength = 10;
        public const string FallbackOrganizationCode = "GEN";

        private static readonly Regex ExamRegex = new(@"^EXM(?<code>[A-Z]{1,10})(?<num>\d{3,9})$", RegexOptions.Compiled | RegexOptions.CultureInvariant);
        private static readonly Regex SubjectRegex = new(@"^SUB(?<num>\d{3,9})$", RegexOptions.Compiled | RegexOptions.CultureInvariant);
        private static readonly Regex TestRegex = new(@"^TST(?<num>\d{4,9})$", RegexOptions.Compiled | RegexOptions.CultureInvariant);
        private static readonly Regex MockTestRegex = new(@"^MCK(?<num>\d{4,9})$", RegexOptions.Compiled | RegexOptions.CultureInvariant);
        private static readonly Regex SharedStimulusRegex = new(@"^STM(?<num>\d{4,9})$", RegexOptions.Compiled | RegexOptions.CultureInvariant);
        private static readonly Regex AdminRegex = new(@"^ADM(?<num>\d{4,9})$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

        public static string Normalize(string? raw) => (raw ?? string.Empty).Trim().ToUpperInvariant();

        public static bool IsValid(BusinessIdEntityType type, string? businessId) =>
            TryParse(type, businessId, out _, out _);

        // Splits a Business ID into its counter key ("EXM:SSC", "SUB", ...) and sequence number.
        public static bool TryParse(BusinessIdEntityType type, string? businessId, out string counterKey, out int number)
        {
            counterKey = string.Empty;
            number = 0;
            var id = Normalize(businessId);
            if (id.Length == 0) return false;

            var regex = RegexFor(type);
            var m = regex.Match(id);
            if (!m.Success) return false;

            if (!int.TryParse(m.Groups["num"].Value, out number) || number < 1) return false;
            counterKey = type == BusinessIdEntityType.Exam
                ? CounterKeyForExam(m.Groups["code"].Value)
                : PrefixFor(type);
            return true;
        }

        public static string PrefixFor(BusinessIdEntityType type) => type switch
        {
            BusinessIdEntityType.Exam => ExamPrefix,
            BusinessIdEntityType.Subject => SubjectPrefix,
            BusinessIdEntityType.Test => TestPrefix,
            BusinessIdEntityType.MockTest => MockTestPrefix,
            BusinessIdEntityType.Admin => AdminPrefix,
            BusinessIdEntityType.SharedStimulus => SharedStimulusPrefix,
            _ => throw new ArgumentOutOfRangeException(nameof(type))
        };

        public static int PadWidthFor(BusinessIdEntityType type) => type switch
        {
            BusinessIdEntityType.Exam => 3,
            BusinessIdEntityType.Subject => 3,
            _ => 4
        };

        public static string CounterKeyForExam(string organizationCode) => $"EXM:{organizationCode}";

        // Counter key for a non-exam entity type is just its prefix; for an exam it also depends
        // on the organization code, so SSC and RRB each have their own 001, 002, ... sequence.
        public static string CounterKeyFor(BusinessIdEntityType type, string? organizationCode = null) =>
            type == BusinessIdEntityType.Exam
                ? CounterKeyForExam(organizationCode ?? FallbackOrganizationCode)
                : PrefixFor(type);

        public static string Format(BusinessIdEntityType type, int number, string? organizationCode = null)
        {
            var digits = number.ToString().PadLeft(PadWidthFor(type), '0');
            return type == BusinessIdEntityType.Exam
                ? $"{ExamPrefix}{organizationCode ?? FallbackOrganizationCode}{digits}"
                : $"{PrefixFor(type)}{digits}";
        }

        public static bool TryParseEntityType(string? raw, out BusinessIdEntityType type)
        {
            type = default;
            var key = (raw ?? string.Empty).Trim().Replace("-", "").Replace("_", "").Replace(" ", "").ToLowerInvariant();
            switch (key)
            {
                case "exam": case "exams": type = BusinessIdEntityType.Exam; return true;
                case "subject": case "subjects": type = BusinessIdEntityType.Subject; return true;
                case "test": case "tests": case "practicetest": case "practicetests": type = BusinessIdEntityType.Test; return true;
                case "mocktest": case "mocktests": case "mock": type = BusinessIdEntityType.MockTest; return true;
                case "admin": case "admins": type = BusinessIdEntityType.Admin; return true;
                case "sharedstimulus": case "sharedstimuli": case "stimulus": case "stimuli": type = BusinessIdEntityType.SharedStimulus; return true;
                default: return false;
            }
        }

        public static string ExampleFor(BusinessIdEntityType type) => type switch
        {
            BusinessIdEntityType.Exam => "EXMSSC001",
            BusinessIdEntityType.Subject => "SUB001",
            BusinessIdEntityType.Test => "TST0001",
            BusinessIdEntityType.MockTest => "MCK0001",
            BusinessIdEntityType.Admin => "ADM0001",
            BusinessIdEntityType.SharedStimulus => "STM0001",
            _ => string.Empty
        };

        private static Regex RegexFor(BusinessIdEntityType type) => type switch
        {
            BusinessIdEntityType.Exam => ExamRegex,
            BusinessIdEntityType.Subject => SubjectRegex,
            BusinessIdEntityType.Test => TestRegex,
            BusinessIdEntityType.MockTest => MockTestRegex,
            BusinessIdEntityType.Admin => AdminRegex,
            BusinessIdEntityType.SharedStimulus => SharedStimulusRegex,
            _ => throw new ArgumentOutOfRangeException(nameof(type))
        };
    }

    // Decides the organization code that goes into an Exam's Business ID (EXM + THIS + 001).
    //
    // Resolution order -- deterministic, never a blind substring of the exam name:
    //   1. The exam's assigned Organization (Exam.OrganizationId): its leading word(s) looked up in
    //      the map below.
    //   2. No Organization (or an unmapped one)? The exam NAME's leading word(s), looked up in the
    //      same map ("UPSSSC PET" -> UPSSSC -> UP, "Bihar Police Constable" -> BIHAR POLICE -> BP).
    //   3. Organization name is unmapped but present: its letters only, capped at 10 chars
    //      (Organization "NABARD" -> NABARD), so an org nobody thought to list still gets a stable,
    //      readable prefix instead of a generic one.
    //   4. Nothing recognisable: "GEN".
    //
    // Once a Business ID has been issued it is permanent (only a SuperAdmin can change it), so this
    // map is the ONE place to review before the first backfill runs -- see BUSINESS_IDS_REPORT.md.
    public static class ExamOrganizationCodes
    {
        // Keys are UPPERCASE, single-space-separated words. Longest match wins (2 words, then 1).
        private static readonly Dictionary<string, string> Map = new(StringComparer.Ordinal)
        {
            ["SSC"] = "SSC",
            ["RRB"] = "RRB",
            ["RAILWAY"] = "RRB",
            ["RAILWAYS"] = "RRB",
            ["RRC"] = "RRC",
            ["UPSSSC"] = "UP",
            ["UP POLICE"] = "UP",
            ["UPPRPB"] = "UPPR",
            ["UPPSC"] = "UPPSC",
            ["UPSC"] = "UPSC",
            ["BPSC"] = "BPSC",
            ["BSSC"] = "BSSC",
            ["BIHAR POLICE"] = "BP",
            ["IBPS"] = "IBPS",
            ["SBI"] = "SBI",
            ["RBI"] = "RBI",
            ["LIC"] = "LIC",
            ["NABARD"] = "NABARD",
            ["DELHI POLICE"] = "DP",
            ["DSSSB"] = "DSSSB",
            ["MPPSC"] = "MPPSC",
            ["RPSC"] = "RPSC",
            ["HSSC"] = "HSSC",
            ["HPSC"] = "HPSC",
            ["NTA"] = "NTA",
            ["CTET"] = "CTET",
            ["NDA"] = "NDA",
            ["CDS"] = "CDS",
        };

        public static string Resolve(string? organizationName, string? examName)
        {
            var org = Words(organizationName);
            var fromOrg = Lookup(org);
            if (fromOrg != null) return fromOrg;

            var fromExam = Lookup(Words(examName));
            if (fromExam != null) return fromExam;

            if (org.Count > 0)
            {
                var letters = LettersOnly(string.Join("", org));
                if (letters.Length > 0)
                    return letters.Length > BusinessIdFormats.MaxOrganizationCodeLength
                        ? letters[..BusinessIdFormats.MaxOrganizationCodeLength]
                        : letters;
            }

            return BusinessIdFormats.FallbackOrganizationCode;
        }

        // Only the LEADING words are tried (2 then 1): an organization is "SSC" or "SSC - Staff
        // Selection Commission", and an exam name is "<ORG> <EXAM>" ("SSC CGL") -- the body's name
        // is always first, never buried in the middle.
        private static string? Lookup(List<string> words)
        {
            if (words.Count == 0) return null;
            if (words.Count >= 2 && Map.TryGetValue($"{words[0]} {words[1]}", out var two)) return two;
            return Map.TryGetValue(words[0], out var one) ? one : null;
        }

        private static List<string> Words(string? raw)
        {
            var sb = new StringBuilder();
            foreach (var ch in (raw ?? string.Empty).ToUpperInvariant())
                sb.Append(char.IsLetterOrDigit(ch) ? ch : ' ');
            return sb.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList();
        }

        private static string LettersOnly(string s)
        {
            var sb = new StringBuilder();
            foreach (var ch in s) if (ch is >= 'A' and <= 'Z') sb.Append(ch);
            return sb.ToString();
        }
    }
}
