using System.Collections.Concurrent;

namespace ScoramAPI.Services
{
    /// <summary>
    /// "Is this person looking at THIS direct-message thread right now?" -- the signal that lets the
    /// server skip a notification (and its push) for a message the recipient is already reading live.
    ///
    /// Deliberately separate from <see cref="IDmPresenceService"/>: that one answers "is the student
    /// in the Messages area at all" (Active now / Last seen); this one is per-conversation and only
    /// ever consulted to suppress notifications. Group chat needs no equivalent -- its room presence
    /// (IChatPresenceService, driven by JoinRoomGroup/LeaveRoomGroup) already means "has that room's
    /// chat open".
    ///
    /// Keyed by connection id so a closed tab / dropped connection can never leave a stale "viewing"
    /// behind (ChatHub.OnDisconnectedAsync calls <see cref="Close"/>). In-memory per instance, like
    /// the non-Redis presence services: with several instances, a recipient connected to a DIFFERENT
    /// instance is simply treated as "not viewing" and still gets the notification -- the safe failure
    /// direction (a redundant notification, never a missing one).
    /// </summary>
    public interface IDmViewingService
    {
        void Open(string connectionId, Guid userId, Guid conversationId);
        void Close(string connectionId);
        bool IsViewing(Guid userId, Guid conversationId);
    }

    public class DmViewingService : IDmViewingService
    {
        private readonly ConcurrentDictionary<string, (Guid UserId, Guid ConversationId)> _byConnection = new();

        // A connection views one thread at a time -- opening another replaces the previous one.
        public void Open(string connectionId, Guid userId, Guid conversationId) =>
            _byConnection[connectionId] = (userId, conversationId);

        public void Close(string connectionId) => _byConnection.TryRemove(connectionId, out _);

        public bool IsViewing(Guid userId, Guid conversationId) =>
            _byConnection.Values.Any(v => v.UserId == userId && v.ConversationId == conversationId);
    }
}
