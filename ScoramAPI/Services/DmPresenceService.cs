using System.Collections.Concurrent;

namespace ScoramAPI.Services
{
    // DIRECT MESSAGES -- "Active now" presence, scoped to whenever a student has their Messages/DM
    // screen open (see ChatHub.EnterDmSection/LeaveDmSection) -- NOT the same thing as "app is open"
    // or "socket is connected" (the website keeps its hub connection alive for the whole session, for
    // @mentions/notifications; a student browsing PYP with the site open shouldn't show as DM-active).
    //
    // Pure in-memory Singleton, same reasoning as ChatPresenceService: presence itself is ephemeral,
    // nothing here worth persisting. What IS persisted is the *last seen* timestamp, written to
    // User.DmLastSeenAt the moment a student's last DM-section connection drops -- that's what lets a
    // now-offline student still show "Last seen 5m ago" instead of nothing.
    //
    // Simpler shape than ChatPresenceService: that one tracks (room, user) pairs because a single
    // connection can be present in several rooms at once. DM-section presence has no such dimension --
    // a connection is either in the Messages section for its one authenticated user, or it isn't -- so
    // this only ever needs a straight userId -> connectionIds map.
    public interface IDmPresenceService
    {
        // True if this was the user's first tracked DM-section connection (i.e. they just went
        // online), as opposed to opening a second tab/device while already counted online.
        bool AddPresence(Guid userId, string connectionId);

        // True if that was the user's last tracked DM-section connection (i.e. they just went
        // offline).
        bool RemovePresence(Guid userId, string connectionId);

        // Called from OnDisconnectedAsync, which only knows the connectionId (the socket dropped
        // without a chance to call LeaveDmSection first, e.g. app killed/network lost). Returns the
        // userId if that connection being gone means they just went offline, null if they weren't
        // tracked as DM-present on this connection at all (never called EnterDmSection) or still have
        // another connection open.
        Guid? RemoveConnection(string connectionId);

        bool IsOnline(Guid userId);
    }

    public class DmPresenceService : IDmPresenceService
    {
        private readonly ConcurrentDictionary<Guid, ConcurrentDictionary<string, byte>> _presence = new();
        private readonly ConcurrentDictionary<string, Guid> _connectionOwner = new();

        public bool AddPresence(Guid userId, string connectionId)
        {
            var connections = _presence.GetOrAdd(userId, _ => new ConcurrentDictionary<string, byte>());
            connections.TryAdd(connectionId, 0);
            _connectionOwner.TryAdd(connectionId, userId);
            return connections.Count == 1;
        }

        public bool RemovePresence(Guid userId, string connectionId)
        {
            _connectionOwner.TryRemove(connectionId, out _);

            if (!_presence.TryGetValue(userId, out var connections)) return false;
            connections.TryRemove(connectionId, out _);
            if (!connections.IsEmpty) return false;

            _presence.TryRemove(userId, out _);
            return true;
        }

        public Guid? RemoveConnection(string connectionId)
        {
            if (!_connectionOwner.TryGetValue(connectionId, out var userId)) return null;
            return RemovePresence(userId, connectionId) ? userId : null;
        }

        public bool IsOnline(Guid userId) =>
            _presence.TryGetValue(userId, out var connections) && !connections.IsEmpty;
    }
}
