using System.Collections.Concurrent;

namespace ScoramAPI.Services
{
    // GROUP CHAT -- "Online user list" (SRS chat feature). Pure in-memory, registered as a Singleton
    // (see Program.cs) -- presence is inherently ephemeral (a server restart legitimately means
    // "everyone reconnects", there's nothing here worth persisting to the database).
    //
    // Deliberately NOT the same thing as SignalR's own "room-{roomId}" group membership (see
    // ChatHub): that group also includes students who are connected to the app with the room's chat
    // view closed, because OnConnectedAsync pre-joins every room a student belongs to so message
    // delivery works without a round-trip. "Online in this room" specifically means they currently
    // have that room's chat screen open, which is exactly when the frontend calls
    // JoinRoomGroup/LeaveRoomGroup (see GroupChat.jsx's RoomChatView mount/unmount effect).
    public interface IChatPresenceService
    {
        // True if this was the user's first tracked connection in this room (i.e. they just went
        // online there, as opposed to opening a second tab on an already-open room).
        bool AddPresence(Guid roomId, Guid userId, string connectionId);

        // True if that was the user's last tracked connection in this room (i.e. they just went
        // offline there).
        bool RemovePresence(Guid roomId, Guid userId, string connectionId);

        // Called once from OnDisconnectedAsync -- cleans up every (room, user) pair this connection
        // was registered under, in one pass, and reports which rooms actually lost a user (so the hub
        // knows which rooms need a presence broadcast; a user with two tabs open in the same room
        // shouldn't trigger one just because one tab closed).
        List<(Guid RoomId, Guid UserId)> RemoveConnection(string connectionId);

        List<Guid> GetOnlineUserIds(Guid roomId);

        // O(1) count without materializing the full user-ID list -- used for the room list view
        // (every room's card shows an online count; that view has no need for who, just how many).
        int GetOnlineCount(Guid roomId);
    }

    public class ChatPresenceService : IChatPresenceService
    {
        // roomId -> userId -> connectionIds currently "present" in that room. A ConcurrentDictionary
        // with a throwaway byte value stands in for a concurrent hash set (no such type in the BCL).
        private readonly ConcurrentDictionary<Guid, ConcurrentDictionary<Guid, ConcurrentDictionary<string, byte>>> _presence = new();

        // connectionId -> the (roomId, userId) pairs it's registered under. Exists purely so
        // RemoveConnection can clean up in O(rooms this connection was in) instead of scanning every
        // room this server process has ever seen presence for.
        private readonly ConcurrentDictionary<string, ConcurrentDictionary<(Guid RoomId, Guid UserId), byte>> _connectionIndex = new();

        public bool AddPresence(Guid roomId, Guid userId, string connectionId)
        {
            var usersInRoom = _presence.GetOrAdd(roomId, _ => new ConcurrentDictionary<Guid, ConcurrentDictionary<string, byte>>());
            var connections = usersInRoom.GetOrAdd(userId, _ => new ConcurrentDictionary<string, byte>());
            connections.TryAdd(connectionId, 0);

            var index = _connectionIndex.GetOrAdd(connectionId, _ => new ConcurrentDictionary<(Guid, Guid), byte>());
            index.TryAdd((roomId, userId), 0);

            return connections.Count == 1;
        }

        public bool RemovePresence(Guid roomId, Guid userId, string connectionId)
        {
            if (_connectionIndex.TryGetValue(connectionId, out var index))
                index.TryRemove((roomId, userId), out _);

            if (!_presence.TryGetValue(roomId, out var usersInRoom)) return false;
            if (!usersInRoom.TryGetValue(userId, out var connections)) return false;

            connections.TryRemove(connectionId, out _);
            if (!connections.IsEmpty) return false;

            usersInRoom.TryRemove(userId, out _);
            return true;
        }

        public List<(Guid RoomId, Guid UserId)> RemoveConnection(string connectionId)
        {
            var wentOffline = new List<(Guid, Guid)>();
            if (!_connectionIndex.TryRemove(connectionId, out var index)) return wentOffline;

            foreach (var (roomId, userId) in index.Keys)
            {
                if (RemovePresence(roomId, userId, connectionId))
                    wentOffline.Add((roomId, userId));
            }
            return wentOffline;
        }

        public List<Guid> GetOnlineUserIds(Guid roomId) =>
            _presence.TryGetValue(roomId, out var usersInRoom) ? usersInRoom.Keys.ToList() : new List<Guid>();

        public int GetOnlineCount(Guid roomId) =>
            _presence.TryGetValue(roomId, out var usersInRoom) ? usersInRoom.Count : 0;
    }
}
