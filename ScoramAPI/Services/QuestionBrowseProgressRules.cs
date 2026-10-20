using ScoramAPI.DTOs;

namespace ScoramAPI.Services
{
    // Database-free rules for Question Bank browsing progress, kept separate so they are easy to unit
    // test and so the controller stays a thin orchestration layer.
    public static class QuestionBrowseProgressRules
    {
        public const int MaxAnswersPerRequest = 100;
        public const int MaxFiltersJsonLength = 4000;
        public const int MaxAnswersReturned = 5000;

        // A client clock running ahead must not be able to "pin" an answer so that later, correct writes
        // are ignored forever -- timestamps are clamped to just after server time.
        public static readonly TimeSpan MaxClockSkew = TimeSpan.FromMinutes(5);

        /// <summary>"a" / " B " -> "A"; anything other than A-D -> null.</summary>
        public static string? NormalizeOption(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return null;
            var s = raw.Trim().ToUpperInvariant();
            return s.Length == 1 && s[0] >= 'A' && s[0] <= 'D' ? s : null;
        }

        public static DateTime ClampClientTime(DateTime? clientTime, DateTime utcNow)
        {
            if (clientTime is null) return utcNow;
            var t = clientTime.Value.Kind == DateTimeKind.Local ? clientTime.Value.ToUniversalTime()
                : DateTime.SpecifyKind(clientTime.Value, DateTimeKind.Utc);
            var ceiling = utcNow + MaxClockSkew;
            return t > ceiling ? ceiling : t;
        }

        /// <summary>
        /// Collapses a batch to ONE valid answer per question -- the one with the latest client time
        /// (ties: the later entry in the list, i.e. the most recent tap). Invalid options are dropped and
        /// counted as ignored by the caller.
        /// </summary>
        public static Dictionary<Guid, (string Option, DateTime At)> LatestPerQuestion(
            IEnumerable<QuestionBrowseAnswerInDto> answers, DateTime utcNow)
        {
            var result = new Dictionary<Guid, (string Option, DateTime At)>();
            foreach (var a in answers)
            {
                if (a.QuestionId == Guid.Empty) continue;
                var option = NormalizeOption(a.SelectedOption);
                if (option is null) continue;
                var at = ClampClientTime(a.ClientUpdatedAt, utcNow);
                if (!result.TryGetValue(a.QuestionId, out var existing) || at >= existing.At)
                    result[a.QuestionId] = (option, at);
            }
            return result;
        }

        /// <summary>Last-write-wins: an incoming write is applied unless a strictly newer one is stored.</summary>
        public static bool ShouldApply(DateTime storedClientTime, DateTime incomingClientTime)
            => incomingClientTime >= storedClientTime;
    }
}
