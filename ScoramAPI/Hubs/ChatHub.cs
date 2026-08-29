using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using ScoramAPI.Data;
using ScoramAPI.Extensions;
using ScoramAPI.Services;

namespace ScoramAPI.Hubs
{
    // Real-time delivery only -- all the actual business logic (persisting messages, banned-word
    // checks, parsing @mentions, file uploads) happens in ChatController over REST; this hub's job is
    // just: put each connection in the right SignalR groups, and let the server push events into them.
    //
    // Groups used:
    //   "room-{roomId}"  -- every active (non-banned) member of that room; receives ReceiveMessage,
    //                        MessageDeleted, PollUpdated, ChatLockChanged
    //   "user-{userId}"  -- this connection's own personal group; receives ReceiveMention regardless of
    //                        which room the mention happened in or whether that room's group has this
    //                        connection in it right now. Also reused by DirectMessagesController to
    //                        push ReceiveDirectMessage -- personal 1:1 chat needs no group of its own.
    //
    // GROUP CHAT -- "Online user list": JoinRoomGroup/LeaveRoomGroup (called when a student opens/
    // closes a specific room's chat view -- see GroupChat.jsx) double as the presence signal, tracked
    // separately in IChatPresenceService since SignalR's own group membership isn't a reliable proxy
    // for that (see OnConnectedAsync below, which pre-joins every room a student belongs to).
    [Authorize]
    public class ChatHub : Hub
    {
        private readonly ScoramDbContext _db;
        private readonly IChatPresenceService _presence;

        public ChatHub(ScoramDbContext db, IChatPresenceService presence)
        {
            _db = db;
            _presence = presence;
        }

        public override async Task OnConnectedAsync()
        {
            var userId = Context.User!.GetUserId();
            await Groups.AddToGroupAsync(Context.ConnectionId, $"user-{userId}");

            // Only students are ever room *members* in the current design -- an admin's JWT would also
            // parse a "sub" claim, but there's no ChatRoomMembership row for admins to look up.
            if (Context.User!.IsInRole("Student"))
            {
                var roomIds = await _db.ChatRoomMemberships
                    .Where(m => m.UserId == userId && !m.IsBanned)
                    .Select(m => m.ChatRoomId)
                    .ToListAsync();

                foreach (var roomId in roomIds)
                    await Groups.AddToGroupAsync(Context.ConnectionId, $"room-{roomId}");
            }

            await base.OnConnectedAsync();
        }

        // Every connection this user has open (multiple tabs/devices) needs to individually drop out
        // of presence for whatever rooms it had open -- otherwise closing one tab would wrongly mark
        // them offline everywhere while another tab is still sitting on the same room.
        public override async Task OnDisconnectedAsync(Exception? exception)
        {
            var wentOffline = _presence.RemoveConnection(Context.ConnectionId);
            foreach (var roomId in wentOffline.Select(x => x.RoomId).Distinct())
                await BroadcastPresenceAsync(roomId);

            await base.OnDisconnectedAsync(exception);
        }

        // Called by the client right after POST /api/chat/rooms/{id}/join succeeds, so this specific
        // live connection starts receiving that room's messages immediately (no reconnect needed).
        // Also called whenever the student opens that room's chat screen (even if already a member
        // from before) -- this second case is what actually drives the online presence signal.
        public async Task JoinRoomGroup(Guid roomId)
        {
            var userId = Context.User!.GetUserId();
            var isActiveMember = await _db.ChatRoomMemberships.AnyAsync(m => m.ChatRoomId == roomId && m.UserId == userId && !m.IsBanned);
            if (!isActiveMember) return;

            await Groups.AddToGroupAsync(Context.ConnectionId, $"room-{roomId}");

            if (_presence.AddPresence(roomId, userId, Context.ConnectionId))
                await BroadcastPresenceAsync(roomId);
        }

        // Called by the client after POST /api/chat/rooms/{id}/leave, for the same "no reconnect
        // needed" reason. Also called when the student navigates away from that room's chat screen
        // while remaining a member (see GroupChat.jsx) -- this is what marks them offline in the
        // presence list without actually removing their room membership.
        public async Task LeaveRoomGroup(Guid roomId)
        {
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"room-{roomId}");

            var userId = Context.User!.GetUserId();
            if (_presence.RemovePresence(roomId, userId, Context.ConnectionId))
                await BroadcastPresenceAsync(roomId);
        }

        private async Task BroadcastPresenceAsync(Guid roomId)
        {
            var onlineUserIds = _presence.GetOnlineUserIds(roomId);
            var users = await _db.Users
                .Where(u => onlineUserIds.Contains(u.Id))
                .Select(u => new { u.Id, u.Username, u.FullName, u.PhotoUrl })
                .ToListAsync();

            await Clients.Group($"room-{roomId}").SendAsync("PresenceUpdated", new { roomId, onlineUsers = users });
        }
    }
}
