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
    //                        push ReceiveDirectMessage, and by EnterDmSection/LeaveDmSection below to
    //                        push DmPresenceUpdated -- personal 1:1 features need no group of their own.
    //
    // GROUP CHAT -- "Online user list": JoinRoomGroup/LeaveRoomGroup (called when a student opens/
    // closes a specific room's chat view -- see GroupChat.jsx) double as the presence signal, tracked
    // separately in IChatPresenceService since SignalR's own group membership isn't a reliable proxy
    // for that (see OnConnectedAsync below, which pre-joins every room a student belongs to).
    //
    // DIRECT MESSAGES -- "Active now" / "Last seen": EnterDmSection/LeaveDmSection are the exact same
    // idiom as JoinRoomGroup/LeaveRoomGroup, deliberately decoupled from the hub connection's own
    // lifecycle -- the website keeps one hub connection alive for the whole session (for
    // @mentions/notifications on every page), so "connection exists" is NOT a valid proxy for "student
    // is looking at Messages right now" the way it incidentally is on mobile (which only connects the
    // hub while its Messages tab is open). Tracked in IDmPresenceService, broadcast only to that
    // student's DM conversation partners (never a global broadcast) via their "user-{id}" groups.
    [Authorize]
    public class ChatHub : Hub
    {
        private readonly ScoramDbContext _db;
        private readonly IChatPresenceService _presence;
        private readonly IDmPresenceService _dmPresence;

        public ChatHub(ScoramDbContext db, IChatPresenceService presence, IDmPresenceService dmPresence)
        {
            _db = db;
            _presence = presence;
            _dmPresence = dmPresence;
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

            // Covers the "app killed / network dropped" case where LeaveDmSection never got called --
            // same reasoning as the room cleanup above, just for the DM-section signal instead.
            var dmUserWentOffline = _dmPresence.RemoveConnection(Context.ConnectionId);
            if (dmUserWentOffline.HasValue)
                await PersistAndBroadcastDmOfflineAsync(dmUserWentOffline.Value);

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

        // Called by the client when the Messages/DM screen becomes the visible one -- on mobile that's
        // GroupChatScreen's DM tab (and staying "in" for as long as a DM thread pushed on top of it is
        // open too); on the website, ConversationsList/ConversationThread's own mount, independent of
        // the hub connection itself which is already alive for other reasons. Multiple tabs/devices
        // calling this for the same user is fine -- only the first one flips them online.
        public async Task EnterDmSection()
        {
            var userId = Context.User!.GetUserId();
            if (_dmPresence.AddPresence(userId, Context.ConnectionId))
                await BroadcastDmPresenceAsync(userId, isOnline: true, lastSeenAt: null);
        }

        // Called when the student navigates away from Messages while remaining connected (the mirror
        // of LeaveRoomGroup) -- e.g. going back to Home. OnDisconnectedAsync above covers the case
        // where the connection itself drops instead.
        public async Task LeaveDmSection()
        {
            var userId = Context.User!.GetUserId();
            if (_dmPresence.RemovePresence(userId, Context.ConnectionId))
                await PersistAndBroadcastDmOfflineAsync(userId);
        }

        // Shared by LeaveDmSection and the OnDisconnectedAsync cleanup path -- both mean the same
        // thing (this student has no DM-section connections left), just discovered two different ways.
        private async Task PersistAndBroadcastDmOfflineAsync(Guid userId)
        {
            var lastSeenAt = DateTime.UtcNow;

            var user = await _db.Users.FindAsync(userId);
            if (user != null)
            {
                user.DmLastSeenAt = lastSeenAt;
                await _db.SaveChangesAsync();
            }

            await BroadcastDmPresenceAsync(userId, isOnline: false, lastSeenAt);
        }

        // Deliberately targeted, never a global broadcast -- only pushed to users who actually share a
        // DirectConversation with this student (their "contacts", for presence purposes), the same way
        // WhatsApp-style presence doesn't tell the whole server who's online. Each partner receives it
        // on their own "user-{id}" group regardless of how many tabs/devices they have open.
        private async Task BroadcastDmPresenceAsync(Guid userId, bool isOnline, DateTime? lastSeenAt)
        {
            var partnerIds = await _db.DirectConversations
                .Where(c => c.UserAId == userId || c.UserBId == userId)
                .Select(c => c.UserAId == userId ? c.UserBId : c.UserAId)
                .ToListAsync();

            foreach (var partnerId in partnerIds)
            {
                await Clients.Group($"user-{partnerId}")
                    .SendAsync("DmPresenceUpdated", new { userId, isOnline, lastSeenAt });
            }
        }
    }
}
