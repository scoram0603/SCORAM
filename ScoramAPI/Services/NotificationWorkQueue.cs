using System.Threading.Channels;

namespace ScoramAPI.Services
{
    // Off-request-path execution for notification work (FCM/Web Push delivery, and My-Exams / broadcast
    // fan-out). Sending a chat message or publishing a test must not wait on Firebase or on creating
    // thousands of notification rows, so those callers just enqueue a delegate here and return.
    //
    // In-process on purpose (System.Threading.Channels, not Redis): it needs no extra infrastructure and
    // works with or without Redis configured. Trade-off, stated plainly: the queue is NOT durable -- a
    // process restart drops whatever hadn't been delivered yet, and with several API instances each one
    // drains only its own queue. That matches how this codebase already treats push ("a missed push is
    // never something that should break the caller's request"): the in-app notification ROW is written
    // synchronously (or by the fan-out job) and remains in the bell; only the system-tray push is
    // best-effort. If stronger delivery guarantees are needed later, swap this for a Redis-backed list
    // like RedisBackgroundJobQueue -- the interface is the only thing callers depend on.
    public interface INotificationWorkQueue
    {
        void Enqueue(Func<IServiceProvider, CancellationToken, Task> work);
        ChannelReader<Func<IServiceProvider, CancellationToken, Task>> Reader { get; }
    }

    public class NotificationWorkQueue : INotificationWorkQueue
    {
        // Unbounded deliberately: the worker itself enqueues (fan-out -> per-user push), and a bounded
        // channel could deadlock a worker waiting on its own full queue.
        private readonly Channel<Func<IServiceProvider, CancellationToken, Task>> _channel =
            Channel.CreateUnbounded<Func<IServiceProvider, CancellationToken, Task>>(
                new UnboundedChannelOptions { SingleReader = true });

        public ChannelReader<Func<IServiceProvider, CancellationToken, Task>> Reader => _channel.Reader;

        public void Enqueue(Func<IServiceProvider, CancellationToken, Task> work) =>
            _channel.Writer.TryWrite(work);
    }

    public class NotificationWorker : BackgroundService
    {
        private readonly INotificationWorkQueue _queue;
        private readonly IServiceScopeFactory _scopes;
        private readonly ILogger<NotificationWorker> _logger;

        public NotificationWorker(INotificationWorkQueue queue, IServiceScopeFactory scopes, ILogger<NotificationWorker> logger)
        {
            _queue = queue;
            _scopes = scopes;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            // Sequential on purpose: keeps DbContext usage simple and gives natural back-pressure on
            // Firebase. Each item gets its own DI scope (the queued delegates need scoped services).
            await foreach (var work in _queue.Reader.ReadAllAsync(stoppingToken))
            {
                try
                {
                    using var scope = _scopes.CreateScope();
                    await work(scope.ServiceProvider, stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    // Never let one bad item stop the loop -- notifications are best-effort.
                    _logger.LogWarning(ex, "A queued notification job failed.");
                }
            }
        }
    }
}
