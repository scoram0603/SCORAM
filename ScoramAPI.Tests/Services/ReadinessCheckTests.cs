using ScoramAPI.Services;
using ScoramAPI.Tests.TestSupport;
using Xunit;

namespace ScoramAPI.Tests.Services
{
    public class ReadinessCheckTests
    {
        private static ReadinessCheck Create(Func<CancellationToken, Task<bool>> probe, Func<DateTimeOffset> clock, ListLogger<ReadinessCheck>? logger = null)
            => new(probe, clock, logger ?? new ListLogger<ReadinessCheck>());

        [Fact]
        public async Task ReachableDatabase_IsReady()
        {
            var check = Create(_ => Task.FromResult(true), () => DateTimeOffset.UtcNow);
            var result = await check.CheckAsync(CancellationToken.None);
            Assert.True(result.Ready);
            Assert.Equal("ok", result.Database);
        }

        [Fact]
        public async Task UnreachableDatabase_IsNotReady()
        {
            var check = Create(_ => Task.FromResult(false), () => DateTimeOffset.UtcNow);
            var result = await check.CheckAsync(CancellationToken.None);
            Assert.False(result.Ready);
            Assert.Equal("unreachable", result.Database);
        }

        [Fact]
        public async Task ProbeException_IsNotReady_AndTheMessageNeverLeaks()
        {
            var logger = new ListLogger<ReadinessCheck>();
            var check = Create(_ => throw new InvalidOperationException("Server=prod-sql.example;User Id=sa;Password=hunter2"), () => DateTimeOffset.UtcNow, logger);

            var result = await check.CheckAsync(CancellationToken.None);

            Assert.False(result.Ready);
            Assert.Equal("unreachable", result.Database);
            Assert.All(logger.Entries, e => Assert.DoesNotContain("hunter2", e.Message));
            Assert.Contains(logger.Entries, e => e.Message.Contains("InvalidOperationException"));
        }

        [Fact]
        public async Task SlowProbe_IsCutOffAtTheTimeout_AndReportedAsTimeout()
        {
            var check = Create(async ct => { await Task.Delay(TimeSpan.FromSeconds(30), ct); return true; }, () => DateTimeOffset.UtcNow);

            var started = DateTime.UtcNow;
            var result = await check.CheckAsync(CancellationToken.None);

            Assert.False(result.Ready);
            Assert.Equal("timeout", result.Database);
            Assert.True(DateTime.UtcNow - started < TimeSpan.FromSeconds(10));
        }

        [Fact]
        public async Task ACallerCancellation_Propagates_InsteadOfBeingReportedAsDatabaseDown()
        {
            using var cts = new CancellationTokenSource();
            var check = Create(async ct => { await Task.Delay(TimeSpan.FromSeconds(30), ct); return true; }, () => DateTimeOffset.UtcNow);
            cts.CancelAfter(50);

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => check.CheckAsync(cts.Token));
        }

        [Fact]
        public async Task ResultIsCachedBriefly_SoPollingCannotHammerTheDatabase()
        {
            var calls = 0;
            var now = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
            var check = Create(_ => { calls++; return Task.FromResult(true); }, () => now);

            await check.CheckAsync(CancellationToken.None);
            now = now.AddSeconds(2);
            await check.CheckAsync(CancellationToken.None);
            Assert.Equal(1, calls);

            now = now.AddSeconds(4); // 6 s after the first probe -> cache expired
            await check.CheckAsync(CancellationToken.None);
            Assert.Equal(2, calls);
        }

        [Fact]
        public async Task ConcurrentCallers_ShareOneProbe()
        {
            var calls = 0;
            var gate = new TaskCompletionSource<bool>();
            var check = Create(async _ => { Interlocked.Increment(ref calls); return await gate.Task; }, () => DateTimeOffset.UtcNow);

            var tasks = Enumerable.Range(0, 5).Select(_ => check.CheckAsync(CancellationToken.None)).ToArray();
            gate.SetResult(true);
            var results = await Task.WhenAll(tasks);

            Assert.Equal(1, calls);
            Assert.All(results, r => Assert.True(r.Ready));
        }
    }
}
