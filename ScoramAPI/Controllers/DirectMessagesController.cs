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
    // Personal 1:1 messaging between students -- sits alongside ChatController's Exam-room chat but
    // is a completely separate model (DirectConversation/DirectMessage, not ChatRoom/ChatMessage).
    // Same real-time approach as room chat though: REST does all persistence/validation, ChatHub's
    // existing "user-{id}" group (already joined by every connection, originally for @mentions) is
    // reused to push new messages. Presence ("Active now" / "Last seen") is the one piece that does
    // need its own hub methods -- see ChatHub.EnterDmSection/LeaveDmSection -- this controller just
    // reads the current snapshot (IDmPresenceService.IsOnline + User.DmLastSeenAt) into
    // ConversationSummaryDto below; live updates after that go straight over the hub, not through here.
    [ApiController]
    [Route("api/directmessages")]
    [Authorize(Roles = "Student")]
    public class DirectMessagesController : ControllerBase
    {
        private readonly ScoramDbContext _db;
        private readonly IFileStorageService _fileStorage;
        private readonly IHubContext<ChatHub> _hub;
        private readonly INotificationService _notifications;
        private readonly IDmPresenceService _dmPresence;

        public DirectMessagesController(ScoramDbContext db, IFileStorageService fileStorage, IHubContext<ChatHub> hub, INotificationService notifications, IDmPresenceService dmPresence)
        {
            _db = db;
            _fileStorage = fileStorage;
            _hub = hub;
            _notifications = notifications;
            _dmPresence = dmPresence;
        }

        // GET /api/directmessages/conversations -- sorted by most recent activity, newest first.
        [HttpGet("conversations")]
        public async Task<ActionResult<List<ConversationSummaryDto>>> ListConversations()
        {
            var userId = User.GetUserId();

            var conversations = await _db.DirectConversations
                .Where(c => c.UserAId == userId || c.UserBId == userId)
                .Include(c => c.UserA)
                .Include(c => c.UserB)
                .OrderByDescending(c => c.LastMessageAt)
                .Select(c => new
                {
                    Conversation = c,
                    OtherUser = c.UserAId == userId ? c.UserB : c.UserA,
                    LastMessage = c.Messages.OrderByDescending(m => m.SentAt).FirstOrDefault(),
                    UnreadCount = c.Messages.Count(m => m.SenderId != userId && !m.IsRead)
                })
                .ToListAsync();

            return Ok(conversations.Select(x => new ConversationSummaryDto
            {
                Id = x.Conversation.Id,
                OtherUserId = x.OtherUser!.Id,
                OtherUsername = x.OtherUser.Username,
                OtherFullName = x.OtherUser.FullName,
                OtherPhotoUrl = x.OtherUser.PhotoUrl,
                LastMessagePreview = PreviewFor(x.LastMessage),
                LastMessageType = x.LastMessage?.MessageType.ToString(),
                LastMessageAt = x.Conversation.LastMessageAt,
                UnreadCount = x.UnreadCount,
                IsOnline = _dmPresence.IsOnline(x.OtherUser!.Id),
                LastSeenAt = x.OtherUser!.DmLastSeenAt
            }).ToList());
        }

        // POST /api/directmessages/conversations/start  { otherUserId } -- get-or-create. Returns the
        // existing thread if these two have already messaged, otherwise creates an empty one so the
        // frontend has a ConversationId to send the first message against.
        [HttpPost("conversations/start")]
        public async Task<ActionResult<ConversationSummaryDto>> StartConversation(StartConversationDto dto)
        {
            var userId = User.GetUserId();
            if (dto.OtherUserId == userId)
                return BadRequest(new { message = "You can't start a conversation with yourself." });

            var otherUser = await _db.Users.FirstOrDefaultAsync(u => u.Id == dto.OtherUserId && u.IsActive);
            if (otherUser == null) return NotFound(new { message = "That user doesn't exist." });

            // Smaller Guid always goes in UserAId -- guarantees "A messages B" and "B messages A"
            // land on the exact same row instead of creating two threads for the same pair.
            var (userAId, userBId) = userId.CompareTo(dto.OtherUserId) < 0
                ? (userId, dto.OtherUserId)
                : (dto.OtherUserId, userId);

            var conversation = await _db.DirectConversations
                .FirstOrDefaultAsync(c => c.UserAId == userAId && c.UserBId == userBId);

            if (conversation == null)
            {
                conversation = new DirectConversation { UserAId = userAId, UserBId = userBId };
                _db.DirectConversations.Add(conversation);
                await _db.SaveChangesAsync();
            }

            return Ok(new ConversationSummaryDto
            {
                Id = conversation.Id,
                OtherUserId = otherUser.Id,
                OtherUsername = otherUser.Username,
                OtherFullName = otherUser.FullName,
                OtherPhotoUrl = otherUser.PhotoUrl,
                LastMessagePreview = null,
                LastMessageType = null,
                LastMessageAt = conversation.LastMessageAt,
                UnreadCount = 0,
                IsOnline = _dmPresence.IsOnline(otherUser.Id),
                LastSeenAt = otherUser.DmLastSeenAt
            });
        }

        // GET /api/directmessages/conversations/{id}/messages?before=&pageSize=  -- newest page first;
        // pass the oldest message's SentAt back as `before` to page further into history.
        [HttpGet("conversations/{id:guid}/messages")]
        public async Task<ActionResult<List<DirectMessageResponseDto>>> GetMessages(Guid id, [FromQuery] DateTime? before, [FromQuery] int pageSize = 30)
        {
            var userId = User.GetUserId();
            if (!await IsParticipant(id, userId)) return Forbid();

            var query = _db.DirectMessages.Include(m => m.Sender).Include(m => m.SharedQuestionBankQuestion).Where(m => m.ConversationId == id);
            if (before.HasValue) query = query.Where(m => m.SentAt < before.Value);

            var messages = await query
                .OrderByDescending(m => m.SentAt)
                .Take(Math.Clamp(pageSize, 1, 100))
                .ToListAsync();

            return Ok(messages.Select(MapMessage).OrderBy(m => m.SentAt).ToList());
        }

        // POST /api/directmessages/conversations/{id}/messages  (multipart/form-data -- Attachment
        // and AttachmentDurationSeconds are optional; the latter only applies to voice notes)
        [HttpPost("conversations/{id:guid}/messages")]
        public async Task<ActionResult<DirectMessageResponseDto>> SendMessage(Guid id, [FromForm] DirectMessageSendDto dto)
        {
            var userId = User.GetUserId();
            var conversation = await _db.DirectConversations.FindAsync(id);
            if (conversation == null) return NotFound(new { message = "Conversation not found." });
            if (!IsParticipant(conversation, userId)) return Forbid();

            if (string.IsNullOrWhiteSpace(dto.MessageText) && dto.Attachment == null)
                return BadRequest(new { message = "Send some text or an attachment." });

            string? attachmentUrl = null;
            var messageType = DirectMessageType.Text;
            if (dto.Attachment != null)
            {
                try
                {
                    var (url, kind) = await _fileStorage.SaveDirectMessageAttachmentAsync(dto.Attachment);
                    attachmentUrl = url;
                    messageType = kind switch
                    {
                        DirectMessageKind.Document => DirectMessageType.Document,
                        DirectMessageKind.Audio => DirectMessageType.Audio,
                        _ => DirectMessageType.Image
                    };
                }
                catch (ArgumentException ex)
                {
                    return BadRequest(new { message = ex.Message });
                }
            }

            var message = new DirectMessage
            {
                ConversationId = id,
                SenderId = userId,
                MessageType = messageType,
                MessageText = dto.MessageText,
                AttachmentUrl = attachmentUrl,
                AttachmentDurationSeconds = messageType == DirectMessageType.Audio ? dto.AttachmentDurationSeconds : null,
                SentAt = DateTime.UtcNow
            };
            _db.DirectMessages.Add(message);
            conversation.LastMessageAt = message.SentAt;
            await _db.SaveChangesAsync();

            var saved = await _db.DirectMessages.Include(m => m.Sender).FirstAsync(m => m.Id == message.Id);
            var responseDto = MapMessage(saved);

            var otherUserId = conversation.UserAId == userId ? conversation.UserBId : conversation.UserAId;
            // Push to both participants' personal groups -- the recipient sees it arrive live, and the
            // sender's own other open tabs/devices stay in sync too (same pattern as room chat, where
            // the sender is also a member of the room group they just posted to).
            // Wrapped in try/catch deliberately: the message is already saved at this point, so a
            // transient hub/connection issue should never turn into a 500 for the sender -- worst case,
            // the recipient just sees it on their next manual refresh instead of instantly.
            try
            {
                await _hub.Clients.Groups(new[] { $"user-{userId}", $"user-{otherUserId}" }).SendAsync("ReceiveDirectMessage", responseDto);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[DirectMessagesController] SignalR push failed for conversation {id}: {ex}");
            }

            await _notifications.CreateAsync(
                otherUserId,
                NotificationType.DirectMessage,
                responseDto.SenderFullName,
                PreviewFor(saved) ?? string.Empty,
                "/chat?tab=messages"
            );

            return Ok(responseDto);
        }

        // POST /api/directmessages/conversations/{id}/share-question -- GROUP CHAT / DM: re-share a
        // Scoram Question Bank question into a DM. Mirrors ChatController.ShareQuestion exactly
        // (same QuestionShare concept, same "resilient snapshot" reasoning) -- see that method's
        // comment for the full rationale.
        [HttpPost("conversations/{id:guid}/share-question")]
        public async Task<ActionResult<DirectMessageResponseDto>> ShareQuestion(Guid id, ShareQuestionToDmDto dto)
        {
            var userId = User.GetUserId();
            var conversation = await _db.DirectConversations.FindAsync(id);
            if (conversation == null) return NotFound(new { message = "Conversation not found." });
            if (!IsParticipant(conversation, userId)) return Forbid();

            var question = await _db.QuestionBankQuestions
                .Include(q => q.ExamMappings).ThenInclude(m => m.Exam)
                .FirstOrDefaultAsync(q => q.Id == dto.QuestionBankQuestionId && q.IsActive);
            if (question == null) return NotFound(new { message = "That question doesn't exist or was removed." });

            var message = new DirectMessage
            {
                ConversationId = id,
                SenderId = userId,
                MessageType = DirectMessageType.QuestionShare,
                MessageText = question.QuestionText.Length > 200 ? question.QuestionText[..200] + "…" : question.QuestionText,
                SharedQuestionBankQuestionId = question.Id,
                SharedQuestionExamName = question.ExamMappings.Select(m => m.Exam?.Name).FirstOrDefault(n => n != null),
                SentAt = DateTime.UtcNow
            };
            _db.DirectMessages.Add(message);
            conversation.LastMessageAt = message.SentAt;
            await _db.SaveChangesAsync();

            var saved = await _db.DirectMessages
                .Include(m => m.Sender)
                .Include(m => m.SharedQuestionBankQuestion)
                .FirstAsync(m => m.Id == message.Id);
            var responseDto = MapMessage(saved);

            var otherUserId = conversation.UserAId == userId ? conversation.UserBId : conversation.UserAId;
            try
            {
                await _hub.Clients.Groups(new[] { $"user-{userId}", $"user-{otherUserId}" }).SendAsync("ReceiveDirectMessage", responseDto);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[DirectMessagesController] SignalR push failed for conversation {id}: {ex}");
            }

            await _notifications.CreateAsync(
                otherUserId,
                NotificationType.DirectMessage,
                responseDto.SenderFullName,
                PreviewFor(saved) ?? string.Empty,
                "/chat?tab=messages"
            );

            return Ok(responseDto);
        }

        // GENERALIZED SHARE -- PYP Paper / Practice Test / Mock Test into a DM. Mirrors
        // ShareQuestion above exactly, and ChatController.ShareContent for the room-chat side --
        // see that method's own comment on the Title/Subtitle snapshot and the "no live exists
        // check for these three types" trade-off.
        [HttpPost("conversations/{id:guid}/share-content")]
        public async Task<ActionResult<DirectMessageResponseDto>> ShareContent(Guid id, ShareContentToDmDto dto)
        {
            var userId = User.GetUserId();
            var conversation = await _db.DirectConversations.FindAsync(id);
            if (conversation == null) return NotFound(new { message = "Conversation not found." });
            if (!IsParticipant(conversation, userId)) return Forbid();

            var (title, subtitle, found) = await ResolveSharedContentAsync(dto.ContentType, dto.ContentId);
            if (!found) return NotFound(new { message = "That item doesn't exist or was removed." });

            var message = new DirectMessage
            {
                ConversationId = id,
                SenderId = userId,
                MessageType = DirectMessageType.ContentShare,
                MessageText = title,
                SharedContentType = dto.ContentType,
                SharedContentId = dto.ContentId,
                SharedContentTitle = title,
                SharedContentSubtitle = subtitle,
                SentAt = DateTime.UtcNow
            };
            _db.DirectMessages.Add(message);
            conversation.LastMessageAt = message.SentAt;
            await _db.SaveChangesAsync();

            var saved2 = await _db.DirectMessages.Include(m => m.Sender).FirstAsync(m => m.Id == message.Id);
            var responseDto2 = MapMessage(saved2);

            var otherUserId2 = conversation.UserAId == userId ? conversation.UserBId : conversation.UserAId;
            try
            {
                await _hub.Clients.Groups(new[] { $"user-{userId}", $"user-{otherUserId2}" }).SendAsync("ReceiveDirectMessage", responseDto2);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[DirectMessagesController] SignalR push failed for conversation {id}: {ex}");
            }

            await _notifications.CreateAsync(
                otherUserId2,
                NotificationType.DirectMessage,
                responseDto2.SenderFullName,
                PreviewFor(saved2) ?? string.Empty,
                "/chat?tab=messages"
            );

            return Ok(responseDto2);
        }

        // DELETE /api/directmessages/messages/{id} -- "unsend": sender-only, soft delete (keeps the
        // row so the thread doesn't have a gap, but wipes the content -- mirrors ChatController's
        // DeleteOwnMessage for room chat, same UX as WhatsApp/Instagram "delete for everyone").
        [HttpDelete("messages/{messageId:guid}")]
        public async Task<IActionResult> DeleteMessage(Guid messageId)
        {
            var userId = User.GetUserId();
            var message = await _db.DirectMessages.FindAsync(messageId);
            if (message == null) return NotFound();
            if (message.SenderId != userId) return Forbid();

            message.IsDeleted = true;
            message.MessageText = null;
            message.AttachmentUrl = null;
            await _db.SaveChangesAsync();

            var conversation = await _db.DirectConversations.FindAsync(message.ConversationId);
            if (conversation != null)
            {
                var otherUserId = conversation.UserAId == userId ? conversation.UserBId : conversation.UserAId;
                try
                {
                    await _hub.Clients.Groups(new[] { $"user-{userId}", $"user-{otherUserId}" })
                        .SendAsync("DirectMessageDeleted", new { messageId = message.Id, conversationId = message.ConversationId });
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[DirectMessagesController] SignalR push failed for delete of {messageId}: {ex}");
                }
            }

            return NoContent();
        }

        // POST /api/directmessages/conversations/{id}/read -- marks every message *not* sent by the
        // current user as read. Called when the thread is opened/scrolled into view.
        [HttpPost("conversations/{id:guid}/read")]
        public async Task<IActionResult> MarkRead(Guid id)
        {
            var userId = User.GetUserId();
            if (!await IsParticipant(id, userId)) return Forbid();

            var unread = await _db.DirectMessages
                .Where(m => m.ConversationId == id && m.SenderId != userId && !m.IsRead)
                .ToListAsync();

            foreach (var m in unread) m.IsRead = true;
            await _db.SaveChangesAsync();

            return NoContent();
        }

        // ---------- helpers ----------

        private async Task<bool> IsParticipant(Guid conversationId, Guid userId)
        {
            var conversation = await _db.DirectConversations.FindAsync(conversationId);
            return conversation != null && IsParticipant(conversation, userId);
        }

        private static bool IsParticipant(DirectConversation conversation, Guid userId) =>
            conversation.UserAId == userId || conversation.UserBId == userId;

        private static string? PreviewFor(DirectMessage? m)
        {
            if (m == null) return null;
            if (m.IsDeleted) return "Message deleted";
            return m.MessageType switch
            {
                DirectMessageType.Image => string.IsNullOrWhiteSpace(m.MessageText) ? "📷 Photo" : m.MessageText,
                DirectMessageType.Document => string.IsNullOrWhiteSpace(m.MessageText) ? "📄 Document" : m.MessageText,
                DirectMessageType.Audio => "🎤 Voice message",
                DirectMessageType.QuestionShare => "🔗 Shared a question",
                DirectMessageType.ContentShare => $"🔗 Shared {m.SharedContentTitle ?? "an item"}",
                _ => m.MessageText
            };
        }

        private static DirectMessageResponseDto MapMessage(DirectMessage m) => new DirectMessageResponseDto
        {
            Id = m.Id,
            ConversationId = m.ConversationId,
            SenderId = m.SenderId,
            SenderUsername = m.Sender?.Username ?? "",
            SenderFullName = m.Sender?.FullName ?? "Unknown",
            MessageType = m.MessageType.ToString(),
            MessageText = m.IsDeleted ? null : m.MessageText,
            AttachmentUrl = m.IsDeleted ? null : m.AttachmentUrl,
            AttachmentDurationSeconds = m.AttachmentDurationSeconds,
            SharedQuestionId = m.SharedQuestionBankQuestionId,
            SharedQuestionExamName = m.IsDeleted ? null : m.SharedQuestionExamName,
            QuestionExists = m.SharedQuestionBankQuestion != null && m.SharedQuestionBankQuestion.IsActive,
            SharedContentType = m.SharedContentType?.ToString(),
            SharedContentId = m.SharedContentId,
            SharedContentTitle = m.IsDeleted ? null : m.SharedContentTitle,
            SharedContentSubtitle = m.IsDeleted ? null : m.SharedContentSubtitle,
            IsRead = m.IsRead,
            IsDeleted = m.IsDeleted,
            SentAt = m.SentAt
        };

        // Mirrors ChatController.ResolveSharedContentAsync exactly (same three content types, same
        // flat Title/Subtitle snapshot) -- a small deliberate duplication rather than a new shared
        // service for three three-line queries, same call as that method's own comment.
        private async Task<(string Title, string? Subtitle, bool Found)> ResolveSharedContentAsync(
            SharedContentType type, Guid contentId)
        {
            switch (type)
            {
                case SharedContentType.PypPaper:
                    var paper = await _db.Papers.Include(p => p.Exam)
                        .FirstOrDefaultAsync(p => p.Id == contentId);
                    if (paper == null) return ("", null, false);
                    return ($"{paper.Exam?.Name ?? "Exam"} {paper.Year} Paper", paper.Tier, true);
                case SharedContentType.Test:
                    var template = await _db.PracticeTestTemplates.FirstOrDefaultAsync(t => t.Id == contentId);
                    if (template == null) return ("", null, false);
                    return (template.Title, "Practice Test", true);
                case SharedContentType.MockTest:
                    var mockTest = await _db.MockTests.FirstOrDefaultAsync(t => t.Id == contentId);
                    if (mockTest == null) return ("", null, false);
                    return (mockTest.Title, mockTest.ExamName, true);
                default:
                    return ("", null, false);
            }
        }
    }
}
