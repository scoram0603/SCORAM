using StackExchange.Redis;

namespace ScoramAPI.Services
{
    // Redis-backed twin of DmPresenceService -- same public contract (IDmPresenceService) and
    // semantics, correct across multiple app instances. See RedisChatPresenceService's own comment
    // for the general reasoning (registered only when Redis is configured/reachable; same known
    // limitation around a hard process kill leaving stale entries, mitigated the same way).
    //
    // Data model, simpler than the chat version since DM-section presence has no per-room dimension:
    //   dm:user:{userId}:connections -- a Redis SET of connectionIds currently in the DM section for
    //                                   that user.
    //   dm:conn:{connectionId}       -- a plain STRING holding the one userId this connection is
    //                                   registered under (a connection belongs to exactly one
    //                                   authenticated user, unlike the chat version's per-room set).
    public class RedisDmPresenceService : IDmPresenceService
    {
        private static readonly TimeSpan ConnectionIndexTtl = TimeSpan.FromHours(24);
        private readonly IConnectionMultiplexer _redis;

        public RedisDmPresenceService(IConnectionMultiplexer redis)
        {
            _redis = redis;
        }

        public bool AddPresence(Guid userId, string connectionId)
        {
            var db = _redis.GetDatabase();
            var setKey = UserKey(userId);
            db.SetAdd(setKey, connectionId);
            db.StringSet(ConnKey(connectionId), userId.ToString(), ConnectionIndexTtl);
            return db.SetLength(setKey) == 1;
        }

        public bool RemovePresence(Guid userId, string connectionId)
        {
            var db = _redis.GetDatabase();
            db.KeyDelete(ConnKey(connectionId));

            var setKey = UserKey(userId);
            db.SetRemove(setKey, connectionId);
            if (db.SetLength(setKey) == 0)
            {
                db.KeyDelete(setKey); // tidy up rather than leave an empty set lingering forever
                return true;
            }
            return false;
        }

        public Guid? RemoveConnection(string connectionId)
        {
            var db = _redis.GetDatabase();
            var owner = db.StringGet(ConnKey(connectionId));
            if (owner.IsNullOrEmpty || !Guid.TryParse(owner.ToString(), out var userId)) return null;

            return RemovePresence(userId, connectionId) ? userId : null;
        }

        public bool IsOnline(Guid userId)
        {
            var db = _redis.GetDatabase();
            return db.SetLength(UserKey(userId)) > 0;
        }

        private static string UserKey(Guid userId) => $"dm:user:{userId}:connections";
        private static string ConnKey(string connectionId) => $"dm:conn:{connectionId}";
    }
}
