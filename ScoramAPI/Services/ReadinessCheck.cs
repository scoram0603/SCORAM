using Microsoft.EntityFrameworkCore;
using ScoramAPI.Data;

namespace ScoramAPI.Services
{
    public readonly record struct ReadinessResult(bool Ready, string Database);

    // Backs GET /health/ready. This is deliberately a SEPARATE endpoint from the existing GET /health:
    //
    //   /health        liveness  -- "the process is up". Does not touch the database, so a slow or paused
    //                  database never makes the host mark the container unhealthy and restart it. Unchanged.
    //   /health/ready  readiness -- "the process can reach its database right now". Intended for deploy
    //                  gates, uptime monitors and manual diagnosis; NOT for the host's restart-on-failure
    //                  health check.
    //
    // The result is cached for a few seconds so that a monitor (or anyone) polling this endpoint cannot
    // turn it into a stream of database connections, and the probe is bounded by a short timeout so the
    // endpoint itself can never hang for the length of a database connect timeout.
    public sealed class ReadinessCheck
    {
        public static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(3);
        public static readonly TimeSpan CacheFor = TimeSpan.FromSeconds(5);

        private readonly Func<CancellationToken, Task<bool>> _probe;
        private readonly Func<DateTimeOffset> _now;
        private readonly ILogger<ReadinessCheck> _logger;
        private readonly SemaphoreSlim _gate = new(1, 1);

        private ReadinessResult _last;
        private DateTimeOffset _lastAt = DateTimeOffset.MinValue;

        // Production constructor: probes the real database through a fresh DI scope.
        public ReadinessCheck(IServiceScopeFactory scopeFactory, ILogger<ReadinessCheck> logger)
            : this(async ct =>
            {
                using var scope = scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<ScoramDbContext>();
                return await db.Database.CanConnectAsync(ct);
            }, () => DateTimeOffset.UtcNow, logger)
        {
        }

        // Test seam: any probe and clock.
        internal ReadinessCheck(Func<CancellationToken, Task<bool>> probe, Func<DateTimeOffset> now, ILogger<ReadinessCheck> logger)
        {
            _probe = probe;
            _now = now;
            _logger = logger;
        }

        public async Task<ReadinessResult> CheckAsync(CancellationToken cancellationToken)
        {
            await _gate.WaitAsync(cancellationToken);
            try
            {
                if (_now() - _lastAt < CacheFor) return _last;

                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeout.CancelAfter(ProbeTimeout);

                ReadinessResult result;
                try
                {
                    var ok = await _probe(timeout.Token);
                    result = new ReadinessResult(ok, ok ? "ok" : "unreachable");
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    _logger.LogWarning("Readiness probe timed out after {TimeoutMs} ms.", (int)ProbeTimeout.TotalMilliseconds);
                    result = new ReadinessResult(false, "timeout");
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    // The exception text can contain the server name / connection details, so only the
                    // exception TYPE goes to the log and nothing at all goes to the HTTP response.
                    _logger.LogWarning("Readiness probe failed: {ExceptionType}", ex.GetType().Name);
                    result = new ReadinessResult(false, "unreachable");
                }

                _last = result;
                _lastAt = _now();
                return result;
            }
            finally
            {
                _gate.Release();
            }
        }
    }
}
