using ScoramAPI.Enums;
using ScoramAPI.Models;

namespace ScoramAPI.Services
{
    /// <summary>
    /// SMART PROGRESS -- the cheap inputs that decide whether re-running the (expensive) plan analysis
    /// could possibly change today's plan. Everything plan generation reads is derived from three things:
    /// the student's released, completed assessments; their Mistake Book revision state; and the clock.
    /// A change in any of them changes this value, so an unchanged value means "nothing new to plan for".
    /// Value equality is what the freshness check relies on.
    /// </summary>
    internal readonly record struct PlanInputFingerprint(
        int CompletedAttempts,
        DateTime? LastAttemptAt,
        int RevisionItems,
        DateTime? LastRevisionItemUpdateAt,
        int RevisionItemsDue);

    /// <summary>What the freshness cache remembers about the last generation pass for one student/day/scope.</summary>
    internal sealed record PlanFreshnessEntry(PlanInputFingerprint Fingerprint, int TaskCount);

    internal static class StudyPlanFreshness
    {
        /// <summary>Upper bound on how long an unchanged fingerprint is trusted. Covers the one thing a
        /// fingerprint cannot see cheaply: pure passage of time (e.g. a well-performing topic crossing the
        /// "not practised for N days" line mid-day), plus any out-of-band data edit.</summary>
        public static readonly TimeSpan RecheckAfter = TimeSpan.FromMinutes(10);

        /// <summary>True when generation provably cannot add anything: the revise task and the timed task
        /// already exist and the practice slots are full. Generation only ever ADDS missing keys, so in
        /// this state skipping it is exactly equivalent to running it.</summary>
        public static bool IsSaturated(IEnumerable<(string TaskKey, StudyTaskType TaskType)> existing, int maxPracticeTasks)
        {
            var hasRevise = false;
            var hasTimed = false;
            var practice = 0;
            foreach (var (key, type) in existing)
            {
                if (type == StudyTaskType.PracticeTopic) practice++;
                else if (key == "revise") hasRevise = true;
                else if (key == "timed") hasTimed = true;
            }
            return hasRevise && hasTimed && practice >= Math.Max(0, maxPracticeTasks);
        }

        /// <summary>Freshness-cache key. The exam scope is part of it because it changes which topics the
        /// plan may recommend.</summary>
        public static string CacheKey(Guid userId, DateOnly day, IReadOnlyCollection<Guid>? examIds)
        {
            var scope = examIds is { Count: > 0 }
                ? string.Join(",", examIds.OrderBy(x => x).Select(x => x.ToString("N")))
                : "-";
            return $"studyplan:fresh:{userId:N}:{day:yyyyMMdd}:{scope}";
        }

        /// <summary>Lock key: one student, one plan day (independent of scope, because the unique index
        /// is per user/day/task key).</summary>
        public static string LockKey(Guid userId, DateOnly day) => $"{userId:N}:{day:yyyyMMdd}";

        public static bool IsFresh(PlanFreshnessEntry? cached, PlanInputFingerprint current, int taskCount)
            => cached != null && cached.Fingerprint == current && cached.TaskCount == taskCount;
    }

    /// <summary>
    /// In-process async mutex per string key, with the dictionary entry removed as soon as nobody holds or
    /// awaits it (no unbounded growth, no head-of-line blocking between different keys). Used so that a
    /// client retry that arrives while the first generation for the same student/day is still running
    /// WAITS for it and then reads the saved plan, instead of starting a second heavy analysis. It is an
    /// optimisation only: correctness across processes/instances still comes from the unique index.
    /// </summary>
    internal sealed class KeyedAsyncLock
    {
        private sealed class Entry
        {
            public readonly SemaphoreSlim Gate = new(1, 1);
            public int Users;
        }

        private readonly object _sync = new();
        private readonly Dictionary<string, Entry> _entries = new(StringComparer.Ordinal);

        /// <summary>Number of keys currently held or awaited (tests/diagnostics).</summary>
        public int ActiveKeys { get { lock (_sync) return _entries.Count; } }

        public async Task<Releaser> AcquireAsync(string key, CancellationToken ct)
        {
            Entry entry;
            bool waited;
            lock (_sync)
            {
                if (!_entries.TryGetValue(key, out entry!))
                {
                    entry = new Entry();
                    _entries[key] = entry;
                }
                waited = entry.Users > 0; // someone already holds or awaits this key
                entry.Users++;
            }

            try
            {
                await entry.Gate.WaitAsync(ct).ConfigureAwait(false);
            }
            catch
            {
                Leave(key, entry);
                throw;
            }
            return new Releaser(this, key, entry, waited);
        }

        private void Leave(string key, Entry entry)
        {
            lock (_sync)
            {
                if (--entry.Users == 0) _entries.Remove(key);
            }
        }

        public sealed class Releaser : IDisposable
        {
            private readonly KeyedAsyncLock _owner;
            private readonly string _key;
            private readonly Entry _entry;
            private int _disposed;

            internal Releaser(KeyedAsyncLock owner, string key, object entry, bool waited)
            {
                _owner = owner;
                _key = key;
                _entry = (Entry)entry;
                Waited = waited;
            }

            /// <summary>True when another holder had the key when this caller asked for it.</summary>
            public bool Waited { get; }

            public void Dispose()
            {
                if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
                _entry.Gate.Release();
                _owner.Leave(_key, _entry);
            }
        }
    }
}
