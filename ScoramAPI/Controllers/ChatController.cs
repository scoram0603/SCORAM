using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using ScoramAPI.Data;
using ScoramAPI.DTOs;
using ScoramAPI.Enums;
using ScoramAPI.Extensions;
using ScoramAPI.Hubs;
using ScoramAPI.Models;
using ScoramAPI.Services;

namespace ScoramAPI.Controllers
{
    // Student-facing group chat: one room per Exam (see ExamsController.Create), joining is optional.
    // Sending goes through this REST endpoint (not a SignalR hub method) so file uploads, banned-word
    // filtering, and mention parsing all happen in one straightforward place; ChatHub's only job is
    // pushing the result out in real time -- see the injected IHubContext<ChatHub> calls below.
    [ApiController]
    [Route("api/chat")]
    [Authorize(Roles = "Student")]
    public class ChatController : ControllerBase
    {
        private static readonly Regex MentionPattern = new(@"@([a-z0-9._]{3,30})", RegexOptions.Compiled);

        private readonly ScoramDbContext _db;
        private readonly IFileStorageService _fileStorage;
        private readonly IHubContext<ChatHub> _hub;
        private readonly INotificationService _notifications;
        private readonly IChatPresenceService _presence;

        public ChatController(ScoramDbContext db, IFileStorageService fileStorage, IHubContext<ChatHub> hub, INotificationService notifications, IChatPresenceService presence)
        {
            _db = db;
            _fileStorage = fileStorage;
            _hub = hub;
            _notifications = notifications;
            _presence = presence;
        }

        // GET /api/chat/rooms?search=
        //
        // GROUP CHAT FIX -- without a search term, only "featured" rooms (admin-curated standalone
        // rooms) and rooms the student has already joined are returned. Exam-linked rooms (created
        // automatically and in bulk -- every Question Bank exam gets one) are IsFeatured = false, so
        // they don't flood this default list; a student finds one by searching for the exam by name,
        // which matches against every room regardless of the featured flag.
        [HttpGet("rooms")]
        public async Task<ActionResult<List<ChatRoomResponseDto>>> ListRooms([FromQuery] string? search = null)
        {
            var userId = User.GetUserId();
            var normalizedSearch = search?.Trim();

            var query = _db.ChatRooms.Include(r => r.Exam).AsQueryable();
            if (!string.IsNullOrWhiteSpace(normalizedSearch))
                query = query.Where(r => r.Name.Contains(normalizedSearch));

            var rooms = await query
                .Select(r => new
                {
                    Room = r,
                    MemberCount = r.Memberships.Count(m => !m.IsBanned),
                    Membership = r.Memberships.FirstOrDefault(m => m.UserId == userId)
                })
                .ToListAsync();

            var result = rooms.Select(x => new ChatRoomResponseDto
            {
                Id = x.Room.Id,
                ExamId = x.Room.ExamId,
                ExamName = x.Room.Exam?.Name ?? x.Room.Name,
                ExamLogoUrl = x.Room.Exam?.LogoUrl,
                IconUrl = x.Room.Exam?.LogoUrl ?? x.Room.IconUrl,
                PostPermission = x.Room.PostPermission.ToString(),
                Description = x.Room.Description,
                IsFeatured = x.Room.IsFeatured,
                IsChatDisabled = x.Room.IsChatDisabled,
                Language = x.Room.Language,
                Rules = x.Room.Rules,
                MemberCount = x.MemberCount,
                OnlineCount = _presence.GetOnlineCount(x.Room.Id),
                IsMember = x.Membership != null && !x.Membership.IsBanned,
                IsBanned = x.Membership?.IsBanned ?? false,
                CreatedAt = x.Room.CreatedAt
            });

            if (string.IsNullOrWhiteSpace(normalizedSearch))
                result = result.Where(r => r.IsFeatured || r.IsMember);

            return Ok(result.ToList());
        }

        // POST /api/chat/rooms/{id}/join
        [HttpPost("rooms/{id:guid}/join")]
        public async Task<IActionResult> JoinRoom(Guid id)
        {
            var userId = User.GetUserId();
            var room = await _db.ChatRooms.FindAsync(id);
            if (room == null) return NotFound(new { message = "Room not found." });

            var membership = await _db.ChatRoomMemberships.FirstOrDefaultAsync(m => m.ChatRoomId == id && m.UserId == userId);
            if (membership != null)
            {
                if (membership.IsBanned)
                    return Forbid();
                return Ok(new { message = "Already a member." });
            }

            _db.ChatRoomMemberships.Add(new ChatRoomMembership { ChatRoomId = id, UserId = userId, JoinedAt = DateTime.UtcNow });
            await _db.SaveChangesAsync();

            // Durable membership is recorded here; the frontend also calls the ChatHub's
            // JoinRoomGroup(roomId) method right after this succeeds so the *live* SignalR connection
            // starts receiving this room's real-time messages immediately, without needing a reconnect.
            return Ok(new { message = "Joined." });
        }

        // POST /api/chat/rooms/{id}/leave -- a voluntary leave, not a ban; the student can rejoin anytime.
        [HttpPost("rooms/{id:guid}/leave")]
        public async Task<IActionResult> LeaveRoom(Guid id)
        {
            var userId = User.GetUserId();
            var membership = await _db.ChatRoomMemberships.FirstOrDefaultAsync(m => m.ChatRoomId == id && m.UserId == userId);
            if (membership == null || membership.IsBanned) return NotFound();

            _db.ChatRoomMemberships.Remove(membership);
            await _db.SaveChangesAsync();
            return Ok(new { message = "Left the room." });
        }

        // GET /api/chat/rooms/{id}/online -- GROUP CHAT: current snapshot of who has this room's chat
        // screen open right now (see IChatPresenceService / ChatHub for how that's tracked). This is
        // just the initial-load snapshot; after that, the frontend listens for the hub's
        // "PresenceUpdated" event to stay live without re-polling this endpoint.
        [HttpGet("rooms/{id:guid}/online")]
        public async Task<ActionResult<List<MentionableUserDto>>> GetOnlineUsers(Guid id)
        {
            var userId = User.GetUserId();
            if (!await IsActiveMember(id, userId)) return Forbid();

            var onlineUserIds = _presence.GetOnlineUserIds(id);
            var users = await _db.Users
                .Where(u => onlineUserIds.Contains(u.Id))
                .Select(u => new MentionableUserDto { Id = u.Id, Username = u.Username, FullName = u.FullName, PhotoUrl = u.PhotoUrl })
                .ToListAsync();

            return Ok(users);
        }

        // GET /api/chat/rooms/{id}/messages?before=&pageSize=  -- newest page first; pass the oldest
        // message's SentAt back as `before` to page further into history (infinite scroll upward).
        [HttpGet("rooms/{id:guid}/messages")]
        public async Task<ActionResult<List<ChatMessageResponseDto>>> GetMessages(Guid id, [FromQuery] DateTime? before, [FromQuery] int pageSize = 30)
        {
            var userId = User.GetUserId();
            if (!await IsActiveMember(id, userId)) return Forbid();

            var query = _db.ChatMessages
                .Include(m => m.User)
                .Include(m => m.SenderAdmin)
                .Include(m => m.Mentions).ThenInclude(mn => mn.MentionedUser)
                .Include(m => m.Poll).ThenInclude(p => p!.Options).ThenInclude(o => o.Votes)
                .Include(m => m.SharedQuestionBankQuestion)
                .Where(m => m.ChatRoomId == id);

            if (before.HasValue) query = query.Where(m => m.SentAt < before.Value);

            var messages = await query
                .OrderByDescending(m => m.SentAt)
                .Take(Math.Clamp(pageSize, 1, 100))
                .ToListAsync();

            return Ok(messages.Select(m => MapMessage(m, userId)).OrderBy(m => m.SentAt).ToList());
        }

        // POST /api/chat/rooms/{id}/messages  (multipart/form-data -- Attachment is optional)
        [HttpPost("rooms/{id:guid}/messages")]
        public async Task<ActionResult<ChatMessageResponseDto>> SendMessage(Guid id, [FromForm] ChatMessageSendDto dto)
        {
            var userId = User.GetUserId();
            var room = await _db.ChatRooms.FindAsync(id);
            if (room == null) return NotFound(new { message = "Room not found." });
            if (room.IsChatDisabled) return BadRequest(new { message = "This chat has been disabled by an admin." });
            if (!await IsActiveMember(id, userId)) return Forbid();
            // ADMIN GROUP SETTINGS -- "who can send messages". Admin-posted Notices/Polls go through
            // their own separate endpoints (AdminChatController) and are unaffected by this either way.
            if (room.PostPermission == ChatRoomPostPermission.AdminOnly)
                return BadRequest(new { message = "Only admins can post in this room." });

            if (string.IsNullOrWhiteSpace(dto.MessageText) && dto.Attachment == null)
                return BadRequest(new { message = "Send some text or an attachment." });

            if (!string.IsNullOrWhiteSpace(dto.MessageText))
            {
                var bannedWords = await _db.BannedWords.Select(w => w.Word).ToListAsync();
                var hit = bannedWords.FirstOrDefault(w => dto.MessageText!.Contains(w, StringComparison.OrdinalIgnoreCase));
                if (hit != null)
                    return BadRequest(new { message = "Your message contains a restricted word or phrase and wasn't sent." });
            }

            string? attachmentUrl = null;
            var messageType = ChatMessageType.Text;
            if (dto.Attachment != null)
            {
                try
                {
                    var (url, isDocument) = await _fileStorage.SaveChatAttachmentAsync(dto.Attachment);
                    attachmentUrl = url;
                    messageType = isDocument ? ChatMessageType.Document : ChatMessageType.Image;
                }
                catch (ArgumentException ex)
                {
                    return BadRequest(new { message = ex.Message });
                }
            }

            var message = new ChatMessage
            {
                ChatRoomId = id,
                UserId = userId,
                MessageType = messageType,
                MessageText = dto.MessageText,
                AttachmentUrl = attachmentUrl,
                SentAt = DateTime.UtcNow
            };
            _db.ChatMessages.Add(message);
            await _db.SaveChangesAsync();

            var mentionedUserIds = await AttachMentionsAsync(message, id);
            await _db.SaveChangesAsync();

            var saved = await _db.ChatMessages
                .Include(m => m.User)
                .Include(m => m.Mentions).ThenInclude(mn => mn.MentionedUser)
                .FirstAsync(m => m.Id == message.Id);
            var responseDto = MapMessage(saved, userId);

            await _hub.Clients.Group($"room-{id}").SendAsync("ReceiveMessage", responseDto);
            foreach (var mentionedUserId in mentionedUserIds)
            {
                await _hub.Clients.Group($"user-{mentionedUserId}").SendAsync("ReceiveMention", responseDto);
                await _notifications.CreateAsync(
                    mentionedUserId,
                    NotificationType.Mention,
                    $"{responseDto.SenderName} mentioned you",
                    responseDto.MessageText ?? "Tap to view the conversation",
                    "/chat"
                );
            }

            return Ok(responseDto);
        }

        // POST /api/chat/rooms/{id}/share-question -- GROUP CHAT: re-share any Scoram Question Bank
        // question into the room as its own message type (QuestionShare) rather than a plain pasted
        // link, so the frontend can render it as a clickable card with the question snippet.
        [HttpPost("rooms/{id:guid}/share-question")]
        public async Task<ActionResult<ChatMessageResponseDto>> ShareQuestion(Guid id, ShareQuestionDto dto)
        {
            var userId = User.GetUserId();
            var room = await _db.ChatRooms.FindAsync(id);
            if (room == null) return NotFound(new { message = "Room not found." });
            if (room.IsChatDisabled) return BadRequest(new { message = "This chat has been disabled by an admin." });
            if (!await IsActiveMember(id, userId)) return Forbid();
            if (room.PostPermission == ChatRoomPostPermission.AdminOnly)
                return BadRequest(new { message = "Only admins can post in this room." });

            var question = await _db.QuestionBankQuestions
                .Include(q => q.ExamMappings).ThenInclude(m => m.Exam)
                .FirstOrDefaultAsync(q => q.Id == dto.QuestionBankQuestionId && q.IsActive);
            if (question == null) return NotFound(new { message = "That question doesn't exist or was removed." });

            var message = new ChatMessage
            {
                ChatRoomId = id,
                UserId = userId,
                MessageType = ChatMessageType.QuestionShare,
                MessageText = question.QuestionText.Length > 200 ? question.QuestionText[..200] + "…" : question.QuestionText,
                SharedQuestionBankQuestionId = question.Id,
                SharedQuestionExamName = question.ExamMappings.Select(m => m.Exam?.Name).FirstOrDefault(n => n != null),
                SentAt = DateTime.UtcNow
            };
            _db.ChatMessages.Add(message);
            await _db.SaveChangesAsync();

            var saved = await _db.ChatMessages
                .Include(m => m.User)
                .Include(m => m.SharedQuestionBankQuestion)
                .FirstAsync(m => m.Id == message.Id);
            var responseDto = MapMessage(saved, userId);

            await _hub.Clients.Group($"room-{id}").SendAsync("ReceiveMessage", responseDto);
            return Ok(responseDto);
        }
        [HttpDelete("messages/{id:guid}")]
        public async Task<IActionResult> DeleteOwnMessage(Guid id)
        {
            var userId = User.GetUserId();
            var message = await _db.ChatMessages.FindAsync(id);
            if (message == null) return NotFound();
            if (message.UserId != userId) return Forbid();

            message.IsDeleted = true;
            await _db.SaveChangesAsync();

            await _hub.Clients.Group($"room-{message.ChatRoomId}").SendAsync("MessageDeleted", message.Id);
            return NoContent();
        }

        // POST /api/chat/messages/{id}/report
        [HttpPost("messages/{id:guid}/report")]
        public async Task<IActionResult> ReportMessage(Guid id, ChatReportCreateDto dto)
        {
            var userId = User.GetUserId();
            var message = await _db.ChatMessages.FindAsync(id);
            if (message == null) return NotFound();

            _db.ChatReports.Add(new ChatReport
            {
                ChatMessageId = id,
                ReportedByUserId = userId,
                Reason = dto.Reason,
                Status = ChatReportStatus.Pending,
                CreatedAt = DateTime.UtcNow
            });
            message.IsReported = true;
            await _db.SaveChangesAsync();

            return Ok(new { message = "Reported. An admin will review it." });
        }

        // GET /api/chat/rooms/{id}/mentionable-users?q= -- @mention autocomplete, room members only
        [HttpGet("rooms/{id:guid}/mentionable-users")]
        public async Task<ActionResult<List<MentionableUserDto>>> GetMentionableUsers(Guid id, [FromQuery] string? q)
        {
            var query = _db.ChatRoomMemberships
                .Where(m => m.ChatRoomId == id && !m.IsBanned)
                .Select(m => m.User!);

            if (!string.IsNullOrWhiteSpace(q))
                query = query.Where(u => u.Username.Contains(q.ToLower()));

            var users = await query.Take(10).Select(u => new MentionableUserDto { Id = u.Id, Username = u.Username, FullName = u.FullName, PhotoUrl = u.PhotoUrl }).ToListAsync();
            return Ok(users);
        }

        // GET /api/chat/mentions?unreadOnly=
        [HttpGet("mentions")]
        public async Task<ActionResult<List<ChatMentionResponseDto>>> GetMentions([FromQuery] bool unreadOnly = false)
        {
            var userId = User.GetUserId();
            var query = _db.ChatMessageMentions
                .Include(mn => mn.ChatMessage).ThenInclude(m => m!.ChatRoom)
                .Include(mn => mn.ChatMessage).ThenInclude(m => m!.User)
                .Include(mn => mn.ChatMessage).ThenInclude(m => m!.SenderAdmin)
                .Where(mn => mn.MentionedUserId == userId);

            if (unreadOnly) query = query.Where(mn => !mn.IsRead);

            var mentions = await query.OrderByDescending(mn => mn.CreatedAt).Take(50).ToListAsync();

            return Ok(mentions.Select(mn => new ChatMentionResponseDto
            {
                Id = mn.Id,
                ChatMessageId = mn.ChatMessageId,
                ChatRoomId = mn.ChatMessage!.ChatRoomId,
                RoomName = mn.ChatMessage.ChatRoom?.Name ?? "",
                MessageTextPreview = Truncate(mn.ChatMessage.MessageText),
                SenderName = mn.ChatMessage.User?.FullName ?? mn.ChatMessage.SenderAdmin?.FullName ?? "Unknown",
                IsRead = mn.IsRead,
                CreatedAt = mn.CreatedAt
            }).ToList());
        }

        [HttpPatch("mentions/{id:guid}/read")]
        public async Task<IActionResult> MarkMentionRead(Guid id)
        {
            var userId = User.GetUserId();
            var mention = await _db.ChatMessageMentions.FirstOrDefaultAsync(mn => mn.Id == id && mn.MentionedUserId == userId);
            if (mention == null) return NotFound();

            mention.IsRead = true;
            await _db.SaveChangesAsync();
            return NoContent();
        }

        [HttpPatch("mentions/read-all")]
        public async Task<IActionResult> MarkAllMentionsRead()
        {
            var userId = User.GetUserId();
            var unread = await _db.ChatMessageMentions.Where(mn => mn.MentionedUserId == userId && !mn.IsRead).ToListAsync();
            foreach (var mn in unread) mn.IsRead = true;
            await _db.SaveChangesAsync();
            return NoContent();
        }

        // POST /api/chat/polls/{id}/vote
        [HttpPost("polls/{id:guid}/vote")]
        public async Task<ActionResult<ChatPollResponseDto>> Vote(Guid id, ChatPollVoteDto dto)
        {
            var userId = User.GetUserId();
            var poll = await _db.ChatPolls.Include(p => p.Options).ThenInclude(o => o.Votes).FirstOrDefaultAsync(p => p.Id == id);
            if (poll == null) return NotFound();
            if (poll.IsClosed) return BadRequest(new { message = "This poll is closed." });
            if (!await IsActiveMember(poll.ChatRoomId, userId)) return Forbid();

            if (dto.OptionIds.Count == 0) return BadRequest(new { message = "Choose at least one option." });
            if (!poll.AllowMultipleChoices && dto.OptionIds.Count > 1)
                return BadRequest(new { message = "This poll only allows one choice." });

            var validOptionIds = poll.Options.Select(o => o.Id).ToHashSet();
            if (dto.OptionIds.Any(optId => !validOptionIds.Contains(optId)))
                return BadRequest(new { message = "One or more options don't belong to this poll." });

            // Re-voting replaces the previous vote(s) rather than stacking duplicates.
            var existingVotes = poll.Options.SelectMany(o => o.Votes).Where(v => v.UserId == userId).ToList();
            _db.ChatPollVotes.RemoveRange(existingVotes);

            foreach (var optionId in dto.OptionIds)
                _db.ChatPollVotes.Add(new ChatPollVote { ChatPollOptionId = optionId, UserId = userId, VotedAt = DateTime.UtcNow });

            await _db.SaveChangesAsync();

            var updated = await _db.ChatPolls.Include(p => p.Options).ThenInclude(o => o.Votes).FirstAsync(p => p.Id == id);
            var pollDto = MapPoll(updated, userId);

            await _hub.Clients.Group($"room-{poll.ChatRoomId}").SendAsync("PollUpdated", pollDto);
            return Ok(pollDto);
        }

        // ---------- helpers ----------

        private async Task<bool> IsActiveMember(Guid roomId, Guid userId) =>
            await _db.ChatRoomMemberships.AnyAsync(m => m.ChatRoomId == roomId && m.UserId == userId && !m.IsBanned);

        private async Task<List<Guid>> AttachMentionsAsync(ChatMessage message, Guid roomId)
        {
            if (string.IsNullOrWhiteSpace(message.MessageText)) return new List<Guid>();

            var usernames = MentionPattern.Matches(message.MessageText)
                .Select(m => m.Groups[1].Value.ToLowerInvariant())
                .Distinct()
                .ToList();
            if (usernames.Count == 0) return new List<Guid>();

            // Only mention actual members of this room -- @some_random_username who never joined
            // shouldn't generate a phantom notification.
            var memberUserIds = await _db.ChatRoomMemberships
                .Where(m => m.ChatRoomId == roomId && !m.IsBanned)
                .Join(_db.Users, m => m.UserId, u => u.Id, (m, u) => new { u.Id, u.Username })
                .Where(x => usernames.Contains(x.Username))
                .Select(x => x.Id)
                .ToListAsync();

            foreach (var uid in memberUserIds)
                _db.ChatMessageMentions.Add(new ChatMessageMention { ChatMessageId = message.Id, MentionedUserId = uid, CreatedAt = DateTime.UtcNow });

            return memberUserIds;
        }

        private static string? Truncate(string? text, int max = 120) =>
            string.IsNullOrEmpty(text) ? text : (text.Length <= max ? text : text[..max] + "...");

        public static ChatMessageResponseDto MapMessage(ChatMessage m, Guid currentUserId) => new ChatMessageResponseDto
        {
            Id = m.Id,
            ChatRoomId = m.ChatRoomId,
            SenderType = m.SenderAdminId != null ? "Admin" : "Student",
            SenderId = m.SenderAdminId ?? m.UserId ?? Guid.Empty,
            SenderName = m.SenderAdmin?.FullName ?? m.User?.FullName ?? "Unknown",
            SenderUsername = m.User?.Username,
            SenderPhotoUrl = m.User?.PhotoUrl,
            MessageType = m.MessageType.ToString(),
            MessageText = m.IsDeleted ? null : m.MessageText,
            AttachmentUrl = m.IsDeleted ? null : m.AttachmentUrl,
            PollId = m.PollId,
            Poll = m.Poll != null ? MapPoll(m.Poll, currentUserId) : null,
            SharedQuestionId = m.SharedQuestionBankQuestionId,
            SharedQuestionExamName = m.IsDeleted ? null : m.SharedQuestionExamName,
            QuestionExists = m.SharedQuestionBankQuestion != null && m.SharedQuestionBankQuestion.IsActive,
            IsDeleted = m.IsDeleted,
            IsReported = m.IsReported,
            MentionedUsernames = m.Mentions?.Select(mn => mn.MentionedUser?.Username ?? "").Where(u => u != "").ToList() ?? new List<string>(),
            SentAt = m.SentAt
        };

        public static ChatPollResponseDto MapPoll(ChatPoll p, Guid currentUserId) => new ChatPollResponseDto
        {
            Id = p.Id,
            Question = p.Question,
            AllowMultipleChoices = p.AllowMultipleChoices,
            IsClosed = p.IsClosed,
            TotalVotes = p.Options.SelectMany(o => o.Votes).Select(v => v.UserId).Distinct().Count(),
            CreatedAt = p.CreatedAt,
            Options = p.Options.OrderBy(o => o.DisplayOrder).Select(o => new ChatPollOptionResponseDto
            {
                Id = o.Id,
                OptionText = o.OptionText,
                VoteCount = o.Votes.Count,
                HasCurrentUserVoted = o.Votes.Any(v => v.UserId == currentUserId)
            }).ToList()
        };
    }
}
