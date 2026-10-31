using ScoramAPI.Enums;
using ScoramAPI.Services;
using Xunit;

namespace ScoramAPI.Tests.StudyPlan
{
    /// <summary>Pure-logic tests for the freshness/locking helpers behind GET /api/study-plan/today
    /// (no database). Needs InternalsVisibleTo ScoramAPI.Tests, which the API csproj already declares.</summary>
    public class StudyPlanGenerationRulesTests
    {
        private static readonly DateTime T0 = new(2026, 10, 28, 10, 0, 0, DateTimeKind.Utc);
        private static (string, StudyTaskType) K(string key, StudyTaskType t) => (key, t);

        [Fact]
        public void Saturated_only_when_revise_timed_and_all_practice_slots_exist()
        {
            var full = new[] { K("revise", StudyTaskType.ReviseMistakes), K("timed", StudyTaskType.TimedSession), K("practice:a|b", StudyTaskType.PracticeTopic), K("practice:c|d", StudyTaskType.PracticeTopic) };
            Assert.True(StudyPlanFreshness.IsSaturated(full, 2));
            Assert.False(StudyPlanFreshness.IsSaturated(full.Where(x => x.Item1 != "revise"), 2));
            Assert.False(StudyPlanFreshness.IsSaturated(full.Where(x => x.Item1 != "timed"), 2));
            Assert.False(StudyPlanFreshness.IsSaturated(full.Take(3), 2));
            Assert.False(StudyPlanFreshness.IsSaturated(Array.Empty<(string, StudyTaskType)>(), 2));
        }

        [Fact]
        public void Freshness_requires_identical_fingerprint_and_task_count()
        {
            var fp = new PlanInputFingerprint(5, T0, 2, T0, 1);
            var cached = new PlanFreshnessEntry(fp, 3);
            Assert.True(StudyPlanFreshness.IsFresh(cached, fp, 3));
            Assert.False(StudyPlanFreshness.IsFresh(null, fp, 3));
            Assert.False(StudyPlanFreshness.IsFresh(cached, fp with { CompletedAttempts = 6 }, 3));
            Assert.False(StudyPlanFreshness.IsFresh(cached, fp with { LastAttemptAt = T0.AddMinutes(1) }, 3));
            Assert.False(StudyPlanFreshness.IsFresh(cached, fp with { LastRevisionItemUpdateAt = T0.AddSeconds(1) }, 3));
            Assert.False(StudyPlanFreshness.IsFresh(cached, fp with { RevisionItemsDue = 2 }, 3));
            Assert.False(StudyPlanFreshness.IsFresh(cached, fp, 4));
        }

        [Fact]
        public void Cache_key_is_per_user_per_day_per_scope()
        {
            var u = Guid.NewGuid(); var a = Guid.NewGuid(); var b = Guid.NewGuid(); var day = new DateOnly(2026, 10, 28);
            Assert.Equal(StudyPlanFreshness.CacheKey(u, day, new[] { a, b }), StudyPlanFreshness.CacheKey(u, day, new[] { b, a }));
            Assert.Equal(StudyPlanFreshness.CacheKey(u, day, null), StudyPlanFreshness.CacheKey(u, day, Array.Empty<Guid>()));
            Assert.NotEqual(StudyPlanFreshness.CacheKey(u, day, null), StudyPlanFreshness.CacheKey(u, day, new[] { a }));
            Assert.NotEqual(StudyPlanFreshness.CacheKey(u, day, null), StudyPlanFreshness.CacheKey(u, day.AddDays(1), null));
            Assert.NotEqual(StudyPlanFreshness.CacheKey(u, day, null), StudyPlanFreshness.CacheKey(Guid.NewGuid(), day, null));
        }

        [Fact]
        public async Task Keyed_lock_is_exclusive_per_key_and_cleans_up()
        {
            var locks = new KeyedAsyncLock();
            int inside = 0, max = 0;
            await Task.WhenAll(Enumerable.Range(0, 30).Select(async _ =>
            {
                using (await locks.AcquireAsync("k", CancellationToken.None))
                {
                    var n = Interlocked.Increment(ref inside);
                    lock (locks) max = Math.Max(max, n);
                    await Task.Delay(2);
                    Interlocked.Decrement(ref inside);
                }
            }));
            Assert.Equal(1, max);
            Assert.Equal(0, locks.ActiveKeys);

            using var other = await locks.AcquireAsync("a", CancellationToken.None);
            var b = locks.AcquireAsync("b", CancellationToken.None);
            Assert.Same(b, await Task.WhenAny(b, Task.Delay(1000)));
            (await b).Dispose();
        }

        [Fact]
        public async Task Keyed_lock_waiter_can_be_cancelled_without_leaking()
        {
            var locks = new KeyedAsyncLock();
            var holder = await locks.AcquireAsync("k", CancellationToken.None);
            using var cts = new CancellationTokenSource(50);
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => locks.AcquireAsync("k", cts.Token));
            holder.Dispose();
            holder.Dispose(); // idempotent
            Assert.Equal(0, locks.ActiveKeys);
        }
    }
}
