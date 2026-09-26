using StackExchange.Redis;

namespace ScoramAPI.Services
{
    // Redis-backed twin of ChatPresenceService -- same public contract (IChatPresenceService), same
    // semantics, but correct when more than one instance of the app is running: a user connected to
    // instance A and one connected to instance B, both present in the same room, both show up in
    // GetOnlineUserIds/GetOnlineCount regardless of which instance answers the request. Registered
    // instead of the in-memory version only when ConnectionStrings:Redis is configured and reachable
    // at startup -- see Program.cs.
    //
    // Data model:
    //   chat:room:{roomId}:users  -- a Redis HASH of userId -> connection count for that user in that
    //                                room. HINCRBY/HDECRBY instead of a set-of-sets (Redis has no
    //                                native nested-set type) -- the count itself is what lets
    //                                "just went online" (0 -> 1) / "just went offline" (1 -> 0) be
    //                                detected atomically without a separate read-then-write.
    //   chat:conn:{connectionId} -- a Redis SET of "{roomId}:{userId}" strings this connection is
    //                                currently registered under, so RemoveConnection can clean up
    //                                without scanning every room this server has ever seen.
    //
    // Known limitation, not present in the in-memory version: if this process is killed hard enough
    // that OnDisconnectedAsync never runs for a connection (e.g. SIGKILL, not a graceful shutdown or
    // an ordinary network drop -- SignalR's transport-level disconnect detection already handles the
    // ordinary case), that connection's entries here would never get cleaned up, inflating a room's
    // online count/list indefinitely. The in-memory version doesn't have this failure mode because a
    // process crash takes its whole ConcurrentDictionary down with it. Mitigated, not eliminated, by
    // the TTL on the connection-index set below -- a periodic reconciliation job (cross-check against
    // each hub's actual live connections) would close the gap fully if this proves to matter in
    // practice, but is a bigger addition than this fix's scope.
    public class RedisChatPresenceService : IChatPresenceService
    {
        private static readonly TimeSpan ConnectionIndexTtl = TimeSpan.FromHours(24);
        private readonly IConnectionMultiplexer _redis;

        public RedisChatPresenceService(IConnectionMultiplexer redis)
        {
            _redis = redis;
        }

        public bool AddPresence(Guid roomId, Guid userId, string connectionId)
        {
            var db = _redis.GetDatabase();
            var newCount = db.HashIncrement(RoomKey(roomId), userId.ToString(), 1);
            db.SetAdd(ConnKey(connectionId), Member(roomId, userId));
            db.KeyExpire(ConnKey(connectionId), ConnectionIndexTtl);
            return newCount == 1;
        }

        public bool RemovePresence(Guid roomId, Guid userId, string connectionId)
        {
            var db = _redis.GetDatabase();
            db.SetRemove(ConnKey(connectionId), Member(roomId, userId));
            return DecrementAndCheckOffline(db, roomId, userId);
        }

        public List<(Guid RoomId, Guid UserId)> RemoveConnection(string connectionId)
        {
            var db = _redis.GetDatabase();
            var connKey = ConnKey(connectionId);
            var entries = db.SetMembers(connKey);
            db.KeyDelete(connKey);

            var wentOffline = new List<(Guid, Guid)>();
            foreach (var entry in entries)
            {
                var (roomId, userId) = ParseMember(entry!);
                if (DecrementAndCheckOffline(db, roomId, userId))
                    wentOffline.Add((roomId, userId));
            }
            return wentOffline;
        }

        public List<Guid> GetOnlineUserIds(Guid roomId)
        {
            var db = _redis.GetDatabase();
            return db.HashKeys(RoomKey(roomId)).Select(k => Guid.Parse(k!)).ToList();
        }

        public int GetOnlineCount(Guid roomId)
        {
            var db = _redis.GetDatabase();
            return (int)db.HashLength(RoomKey(roomId));
        }

        private static bool DecrementAndCheckOffline(IDatabase db, Guid roomId, Guid userId)
        {
            var newCount = db.HashDecrement(RoomKey(roomId), userId.ToString(), 1);
            if (newCount <= 0)
            {
                // HDECRBY on a field already at 0 (shouldn't normally happen, but a duplicate
                // RemovePresence call for the same connection is cheap insurance against it) would
                // otherwise leave a stray "userId: 0" or negative field behind forever.
                db.HashDelete(RoomKey(roomId), userId.ToString());
                return true;
            }
            return false;
        }

        private static string RoomKey(Guid roomId) => $"chat:room:{roomId}:users";
        private static string ConnKey(string connectionId) => $"chat:conn:{connectionId}";
        private static string Member(Guid roomId, Guid userId) => $"{roomId}:{userId}";

        private static (Guid RoomId, Guid UserId) ParseMember(string member)
        {
            var parts = member.Split(':');
            return (Guid.Parse(parts[0]), Guid.Parse(parts[1]));
        }
    }
}
