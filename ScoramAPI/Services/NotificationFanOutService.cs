using Microsoft.EntityFrameworkCore;
using ScoramAPI.Data;
using ScoramAPI.Enums;

namespace ScoramAPI.Services
{
    public enum FanOutAudience
    {
        /// <summary>Active students whose My Exams contain ExamIds (any of them).</summary>
        ExamAudience,
        /// <summary>Every active student.</summary>
        AllStudents,
        /// <summary>Exactly the listed students.</summary>
        SpecificUsers
    }

    public sealed class FanOutRequest
    {
        public FanOutAudience Audience { get; init; }
        public IReadOnlyList<Guid> ExamIds { get; init; } = Array.Empty<Guid>();
        public IReadOnlyList<Guid> UserIds { get; init; } = Array.Empty<Guid>();
        public NotificationType Type { get; init; }
        public string Title { get; init; } = string.Empty;
        public string Body { get; init; } = string.Empty;
        public string LinkUrl { get; init; } = "/";
        public string? EntityType { get; init; }
        public string? EntityId { get; init; }
        /// <summary>Per-event idempotency key; each user's DedupKey becomes this value, so re-running the
        /// same job (publish clicked twice, retry) can never notify anyone twice.</summary>
        public string? DedupKey { get; init; }
    }

    public interface INotificationFanOutService
    {
        /// <summary>Queues the fan-out and returns immediately (the admin request does not wait).</summary>
        void Queue(FanOutRequest request);
    }

    public class NotificationFanOutService : INotificationFanOutService
    {
        private const int BatchSize = 200;
        private readonly INotificationWorkQueue _queue;

        public NotificationFanOutService(INotificationWorkQueue queue) => _queue = queue;

        public void Queue(FanOutRequest request) => _queue.Enqueue((sp, ct) => RunAsync(sp, request, ct));

        private static async Task RunAsync(IServiceProvider rootScope, FanOutRequest request, CancellationToken ct)
        {
            // MY EXAMS: the audience is resolved IN THE DATABASE from UserExamPreferences as it stands at
            // send time -- a student who removed the exam from My Exams is simply not in the result.
            // Existing notification history is never touched. Ordered by Id so batching by "last seen Id"
            // is stable even though students keep changing underneath us.
            var db = rootScope.GetRequiredService<ScoramDbContext>();
            IQueryable<Guid> ids = request.Audience switch
            {
                FanOutAudience.ExamAudience => db.UserExamPreferences
                    .Where(p => request.ExamIds.Contains(p.ExamId) && p.User!.IsActive)
                    .Select(p => p.UserId).Distinct(),
                FanOutAudience.SpecificUsers => db.Users
                    .Where(u => request.UserIds.Contains(u.Id) && u.IsActive)
                    .Select(u => u.Id),
                _ => db.Users.Where(u => u.IsActive).Select(u => u.Id)
            };

            var all = await ids.OrderBy(i => i).ToListAsync(ct);
            var scopes = rootScope.GetRequiredService<IServiceScopeFactory>();

            foreach (var batch in all.Chunk(BatchSize))
            {
                ct.ThrowIfCancellationRequested();
                // Fresh scope per batch so the DbContext change tracker doesn't grow with the audience.
                using var scope = scopes.CreateScope();
                var notifications = scope.ServiceProvider.GetRequiredService<INotificationService>();
                foreach (var userId in batch)
                {
                    await notifications.CreateAsync(userId, new NotificationRequest
                    {
                        Type = request.Type,
                        Title = request.Title,
                        Body = request.Body,
                        LinkUrl = request.LinkUrl,
                        EntityType = request.EntityType,
                        EntityId = request.EntityId,
                        DedupKey = request.DedupKey
                    });
                }
            }
        }
    }
}
