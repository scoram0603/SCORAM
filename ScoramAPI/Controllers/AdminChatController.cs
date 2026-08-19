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
    // Admin-side group chat moderation. Every action here is gated by its own specific permission
    // (see AdminPermission enum) rather than one big "ModerateChat" flag, so e.g. an admin trusted to
    // post exam notices doesn't automatically also get report-handling or member-removal access.
    [ApiController]
    [Route("api/admin/chat")]
    [Authorize(Roles = "Admin,SuperAdmin")]
    public class AdminChatController : ControllerBase
    {
        private readonly ScoramDbContext _db;
        private readonly IAdminPermissionService _permissions;
        private readonly IHubContext<ChatHub> _hub;
        private readonly IFileStorageService _fileStorage;

        public AdminChatController(ScoramDbContext db, IAdminPermissionService permissions, IHubContext<ChatHub> hub, IFileStorageService fileStorage)
        {
            _db = db;
            _permissions = permissions;
            _hub = hub;
            _fileStorage = fileStorage;
        }

        // GET /api/admin/chat/rooms -- any admin can view (not permission-gated, matches the Papers pattern)
        [HttpGet("rooms")]
        public async Task<ActionResult<List<ChatRoomResponseDto>>> ListRooms()
        {
            var rooms = await _db.ChatRooms
                .Include(r => r.Exam)
                .Select(r => new { Room = r, MemberCount = r.Memberships.Count(m => !m.IsBanned) })
                .ToListAsync();

            return Ok(rooms.Select(x => new ChatRoomResponseDto
            {
                Id = x.Room.Id,
                ExamId = x.Room.ExamId,
                ExamName = x.Room.Exam?.Name ?? x.Room.Name,
                ExamLogoUrl = x.Room.Exam?.LogoUrl,
                IconUrl = x.Room.Exam?.LogoUrl ?? x.Room.IconUrl,
                Description = x.Room.Description,
                IsFeatured = x.Room.IsFeatured,
                IsChatDisabled = x.Room.IsChatDisabled,
                PostPermission = x.Room.PostPermission.ToString(),
                Language = x.Room.Language,
                Rules = x.Room.Rules,
                MemberCount = x.MemberCount,
                CreatedAt = x.Room.CreatedAt
            }).ToList());
        }

        // POST /api/admin/chat/rooms  (ManageChatRooms) -- GROUP CHAT: manual room CRUD, for
        // standalone rooms with no Exam (e.g. "Daily Doubt Room", "Current Affairs Room" from the
        // SRS room list) -- exam-linked rooms still only ever come from ExamsController/Question Bank
        // auto-creation, never from here.
        [HttpPost("rooms")]
        public async Task<ActionResult<ChatRoomResponseDto>> CreateRoom(ChatRoomCreateDto dto)
        {
            if (!await _permissions.HasPermissionAsync(User, AdminPermission.ManageChatRooms)) return Forbid();

            var name = dto.Name.Trim();
            if (string.IsNullOrWhiteSpace(name)) return BadRequest(new { message = "Name is required." });
            if (await _db.ChatRooms.AnyAsync(r => r.Name.ToLower() == name.ToLower()))
                return Conflict(new { message = $"A room named \"{name}\" already exists." });

            var room = new ChatRoom
            {
                Name = name,
                Description = dto.Description,
                IsFeatured = dto.IsFeatured,
                CreatedAt = DateTime.UtcNow
            };
            _db.ChatRooms.Add(room);
            await _db.SaveChangesAsync();

            return Ok(new ChatRoomResponseDto
            {
                Id = room.Id,
                ExamId = null,
                ExamName = room.Name,
                Description = room.Description,
                IsFeatured = room.IsFeatured,
                IsChatDisabled = room.IsChatDisabled,
                PostPermission = room.PostPermission.ToString(),
                MemberCount = 0,
                CreatedAt = room.CreatedAt
            });
        }

        // POST /api/admin/chat/rooms/{id}/icon  (ManageChatRooms) -- ADMIN GROUP SETTINGS: room
        // picture. Standalone rooms only -- an exam-linked room shows Exam.LogoUrl instead (see
        // ExamsController.Update for changing that one).
        [HttpPost("rooms/{id:guid}/icon")]
        [RequestSizeLimit(3 * 1024 * 1024)]
        public async Task<ActionResult<ChatRoomResponseDto>> UpdateRoomIcon(Guid id, IFormFile icon)
        {
            if (!await _permissions.HasPermissionAsync(User, AdminPermission.ManageChatRooms)) return Forbid();

            var room = await _db.ChatRooms.Include(r => r.Exam).FirstOrDefaultAsync(r => r.Id == id);
            if (room == null) return NotFound();
            if (room.ExamId != null)
                return BadRequest(new { message = "This room is linked to an exam -- change the exam's logo instead." });

            try
            {
                room.IconUrl = await _fileStorage.SaveImageAsync(icon, "chat-room-icons") ?? room.IconUrl;
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { message = ex.Message });
            }

            await _db.SaveChangesAsync();

            return Ok(new ChatRoomResponseDto
            {
                Id = room.Id,
                ExamId = null,
                ExamName = room.Name,
                IconUrl = room.IconUrl,
                Description = room.Description,
                IsFeatured = room.IsFeatured,
                IsChatDisabled = room.IsChatDisabled,
                PostPermission = room.PostPermission.ToString(),
                MemberCount = await _db.ChatRoomMemberships.CountAsync(m => m.ChatRoomId == room.Id && !m.IsBanned),
                CreatedAt = room.CreatedAt
            });
        }

        // PATCH /api/admin/chat/rooms/{id}  (ManageChatRooms) -- works for both standalone and
        // exam-linked rooms, but Name is only editable on standalone ones: an exam-linked room's Name
        // is a denormalized copy of Exam.Name and renaming it independently would desync the two
        // (rename the Exam itself instead, from Admin > Exams).
        [HttpPatch("rooms/{id:guid}")]
        public async Task<ActionResult<ChatRoomResponseDto>> UpdateRoom(Guid id, ChatRoomUpdateDto dto)
        {
            if (!await _permissions.HasPermissionAsync(User, AdminPermission.ManageChatRooms)) return Forbid();

            var room = await _db.ChatRooms.Include(r => r.Exam).FirstOrDefaultAsync(r => r.Id == id);
            if (room == null) return NotFound();

            if (dto.Name != null)
            {
                if (room.ExamId != null)
                    return BadRequest(new { message = "This room is linked to an exam -- rename the exam instead." });
                var name = dto.Name.Trim();
                if (string.IsNullOrWhiteSpace(name)) return BadRequest(new { message = "Name can't be empty." });
                room.Name = name;
            }

            if (dto.Description != null) room.Description = dto.Description;
            if (dto.IsFeatured.HasValue) room.IsFeatured = dto.IsFeatured.Value;
            if (dto.Language != null) room.Language = dto.Language;
            if (dto.Rules != null) room.Rules = dto.Rules;
            if (dto.PostPermission != null)
            {
                if (!Enum.TryParse<ChatRoomPostPermission>(dto.PostPermission, out var permission))
                    return BadRequest(new { message = "PostPermission must be \"AllMembers\" or \"AdminOnly\"." });
                room.PostPermission = permission;
            }

            await _db.SaveChangesAsync();

            return Ok(new ChatRoomResponseDto
            {
                Id = room.Id,
                ExamId = room.ExamId,
                ExamName = room.Exam?.Name ?? room.Name,
                ExamLogoUrl = room.Exam?.LogoUrl,
                IconUrl = room.Exam?.LogoUrl ?? room.IconUrl,
                Description = room.Description,
                IsFeatured = room.IsFeatured,
                IsChatDisabled = room.IsChatDisabled,
                Language = room.Language,
                Rules = room.Rules,
                PostPermission = room.PostPermission.ToString(),
                MemberCount = await _db.ChatRoomMemberships.CountAsync(m => m.ChatRoomId == room.Id && !m.IsBanned),
                CreatedAt = room.CreatedAt
            });
        }

        // DELETE /api/admin/chat/rooms/{id}  (ManageChatRooms) -- exam-linked rooms can't be deleted
        // independently (delete the exam, if that's really the intent) so this is really only for
        // standalone rooms created via CreateRoom above. Cascades to messages/memberships/polls (see
        // ScoramDbContext's Cascade delete behavior on those relationships).
        [HttpDelete("rooms/{id:guid}")]
        public async Task<IActionResult> DeleteRoom(Guid id)
        {
            if (!await _permissions.HasPermissionAsync(User, AdminPermission.ManageChatRooms)) return Forbid();

            var room = await _db.ChatRooms.FindAsync(id);
            if (room == null) return NotFound();
            if (room.ExamId != null)
                return BadRequest(new { message = "This room is linked to an exam and can't be deleted on its own." });

            _db.ChatRooms.Remove(room);
            await _db.SaveChangesAsync();
            return NoContent();
        }

        // PATCH /api/admin/chat/rooms/{id}/lock  (ToggleChatLock)
        [HttpPatch("rooms/{id:guid}/lock")]
        public async Task<IActionResult> ToggleLock(Guid id, [FromQuery] bool disabled)
        {
            if (!await _permissions.HasPermissionAsync(User, AdminPermission.ToggleChatLock)) return Forbid();

            var room = await _db.ChatRooms.FindAsync(id);
            if (room == null) return NotFound();

            room.IsChatDisabled = disabled;
            await _db.SaveChangesAsync();

            await _hub.Clients.Group($"room-{id}").SendAsync("ChatLockChanged", new { roomId = id, isChatDisabled = disabled });
            return Ok(new { room.Id, room.IsChatDisabled });
        }

        // GET /api/admin/chat/rooms/{id}/members -- any admin can view
        [HttpGet("rooms/{id:guid}/members")]
        public async Task<ActionResult<object>> ListMembers(Guid id)
        {
            var members = await _db.ChatRoomMemberships
                .Include(m => m.User)
                .Where(m => m.ChatRoomId == id)
                .OrderBy(m => m.IsBanned).ThenBy(m => m.JoinedAt)
                .Select(m => new
                {
                    m.UserId,
                    Username = m.User!.Username,
                    FullName = m.User.FullName,
                    m.IsBanned,
                    m.JoinedAt
                })
                .ToListAsync();

            return Ok(members);
        }

        // DELETE /api/admin/chat/rooms/{id}/members/{userId}  (RemoveGroupMembers) -- a permanent ban;
        // the student can't rejoin themselves afterward (see ChatController.JoinRoom's Forbid on IsBanned).
        [HttpDelete("rooms/{id:guid}/members/{userId:guid}")]
        public async Task<IActionResult> RemoveMember(Guid id, Guid userId)
        {
            if (!await _permissions.HasPermissionAsync(User, AdminPermission.RemoveGroupMembers)) return Forbid();

            var membership = await _db.ChatRoomMemberships.FirstOrDefaultAsync(m => m.ChatRoomId == id && m.UserId == userId);
            if (membership == null)
            {
                // Never joined, but ban them pre-emptively so they can't join later either.
                membership = new ChatRoomMembership { ChatRoomId = id, UserId = userId, JoinedAt = DateTime.UtcNow };
                _db.ChatRoomMemberships.Add(membership);
            }

            membership.IsBanned = true;
            membership.BannedAt = DateTime.UtcNow;
            membership.BannedByAdminId = User.GetAdminId();
            await _db.SaveChangesAsync();

            await _hub.Clients.Group($"room-{id}").SendAsync("MemberRemoved", new { roomId = id, userId });
            return Ok(new { message = "Removed from the group." });
        }

        // POST /api/admin/chat/rooms/{id}/notices  (PostNotices)
        [HttpPost("rooms/{id:guid}/notices")]
        public async Task<ActionResult<ChatMessageResponseDto>> PostNotice(Guid id, ChatNoticeCreateDto dto)
        {
            if (!await _permissions.HasPermissionAsync(User, AdminPermission.PostNotices)) return Forbid();

            var room = await _db.ChatRooms.FindAsync(id);
            if (room == null) return NotFound();

            var message = new ChatMessage
            {
                ChatRoomId = id,
                SenderAdminId = User.GetAdminId(),
                MessageType = ChatMessageType.Notice,
                MessageText = dto.MessageText,
                SentAt = DateTime.UtcNow
            };
            _db.ChatMessages.Add(message);
            await _db.SaveChangesAsync();

            var saved = await _db.ChatMessages.Include(m => m.SenderAdmin).FirstAsync(m => m.Id == message.Id);
            var responseDto = ChatController.MapMessage(saved, Guid.Empty);

            await _hub.Clients.Group($"room-{id}").SendAsync("ReceiveMessage", responseDto);
            return Ok(responseDto);
        }

        // POST /api/admin/chat/rooms/{id}/polls  (CreatePolls)
        [HttpPost("rooms/{id:guid}/polls")]
        public async Task<ActionResult<ChatMessageResponseDto>> CreatePoll(Guid id, ChatPollCreateDto dto)
        {
            if (!await _permissions.HasPermissionAsync(User, AdminPermission.CreatePolls)) return Forbid();

            var room = await _db.ChatRooms.FindAsync(id);
            if (room == null) return NotFound();

            var validOptions = dto.Options.Where(o => !string.IsNullOrWhiteSpace(o)).ToList();
            if (validOptions.Count < 2) return BadRequest(new { message = "A poll needs at least 2 options." });

            var poll = new ChatPoll
            {
                ChatRoomId = id,
                CreatedByAdminId = User.GetAdminId(),
                Question = dto.Question,
                AllowMultipleChoices = dto.AllowMultipleChoices,
                CreatedAt = DateTime.UtcNow
            };
            for (int i = 0; i < validOptions.Count; i++)
                poll.Options.Add(new ChatPollOption { OptionText = validOptions[i], DisplayOrder = i });

            _db.ChatPolls.Add(poll);
            await _db.SaveChangesAsync();

            var message = new ChatMessage
            {
                ChatRoomId = id,
                SenderAdminId = User.GetAdminId(),
                MessageType = ChatMessageType.Poll,
                PollId = poll.Id,
                SentAt = DateTime.UtcNow
            };
            _db.ChatMessages.Add(message);
            await _db.SaveChangesAsync();

            var savedMessage = await _db.ChatMessages
                .Include(m => m.SenderAdmin)
                .Include(m => m.Poll).ThenInclude(p => p!.Options).ThenInclude(o => o.Votes)
                .FirstAsync(m => m.Id == message.Id);
            var responseDto = ChatController.MapMessage(savedMessage, Guid.Empty);

            await _hub.Clients.Group($"room-{id}").SendAsync("ReceiveMessage", responseDto);
            return Ok(responseDto);
        }

        // PATCH /api/admin/chat/polls/{id}/close  (CreatePolls)
        [HttpPatch("polls/{id:guid}/close")]
        public async Task<IActionResult> ClosePoll(Guid id)
        {
            if (!await _permissions.HasPermissionAsync(User, AdminPermission.CreatePolls)) return Forbid();

            var poll = await _db.ChatPolls.Include(p => p.Options).ThenInclude(o => o.Votes).FirstOrDefaultAsync(p => p.Id == id);
            if (poll == null) return NotFound();

            poll.IsClosed = true;
            poll.ClosedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();

            var pollDto = ChatController.MapPoll(poll, Guid.Empty);
            await _hub.Clients.Group($"room-{poll.ChatRoomId}").SendAsync("PollUpdated", pollDto);
            return Ok(pollDto);
        }

        // GET /api/admin/chat/reports?status=  (HandleChatReports)
        [HttpGet("reports")]
        public async Task<ActionResult<List<ChatReportResponseDto>>> ListReports([FromQuery] ChatReportStatus? status)
        {
            if (!await _permissions.HasPermissionAsync(User, AdminPermission.HandleChatReports)) return Forbid();

            var query = _db.ChatReports
                .Include(r => r.ChatMessage).ThenInclude(m => m!.ChatRoom)
                .Include(r => r.ReportedByUser)
                .AsQueryable();

            query = status.HasValue ? query.Where(r => r.Status == status.Value) : query.Where(r => r.Status == ChatReportStatus.Pending);

            var reports = await query.OrderByDescending(r => r.CreatedAt).ToListAsync();

            return Ok(reports.Select(r => new ChatReportResponseDto
            {
                Id = r.Id,
                ChatMessageId = r.ChatMessageId,
                ChatRoomId = r.ChatMessage!.ChatRoomId,
                RoomName = r.ChatMessage.ChatRoom?.Name ?? "",
                MessageTextPreview = r.ChatMessage.MessageText,
                ReportedByUsername = r.ReportedByUser?.Username ?? "Unknown",
                Reason = r.Reason,
                Status = r.Status.ToString(),
                ResolutionNote = r.ResolutionNote,
                CreatedAt = r.CreatedAt,
                ResolvedAt = r.ResolvedAt
            }).ToList());
        }

        // PATCH /api/admin/chat/reports/{id}/resolve  (HandleChatReports)
        [HttpPatch("reports/{id:guid}/resolve")]
        public async Task<IActionResult> ResolveReport(Guid id, ChatReportResolveDto dto)
        {
            if (!await _permissions.HasPermissionAsync(User, AdminPermission.HandleChatReports)) return Forbid();

            var report = await _db.ChatReports.Include(r => r.ChatMessage).FirstOrDefaultAsync(r => r.Id == id);
            if (report == null) return NotFound();

            if (!Enum.TryParse<ChatReportStatus>(dto.Status, out var status) || status == ChatReportStatus.Pending)
                return BadRequest(new { message = "Status must be ActionTaken or Dismissed." });

            report.Status = status;
            report.ResolutionNote = dto.ResolutionNote;
            report.ResolvedByAdminId = User.GetAdminId();
            report.ResolvedAt = DateTime.UtcNow;

            if (dto.DeleteMessage && report.ChatMessage != null)
                report.ChatMessage.IsDeleted = true;

            await _db.SaveChangesAsync();

            if (dto.DeleteMessage && report.ChatMessage != null)
                await _hub.Clients.Group($"room-{report.ChatMessage.ChatRoomId}").SendAsync("MessageDeleted", report.ChatMessageId);

            return Ok(new { report.Id, Status = report.Status.ToString() });
        }

        // GET /api/admin/chat/banned-words  (ManageBannedWords)
        [HttpGet("banned-words")]
        public async Task<ActionResult<List<BannedWordResponseDto>>> ListBannedWords()
        {
            if (!await _permissions.HasPermissionAsync(User, AdminPermission.ManageBannedWords)) return Forbid();

            var words = await _db.BannedWords.Include(w => w.AddedByAdmin).OrderBy(w => w.Word).ToListAsync();
            return Ok(words.Select(w => new BannedWordResponseDto
            {
                Id = w.Id,
                Word = w.Word,
                AddedByAdminName = w.AddedByAdmin?.FullName ?? "Unknown",
                CreatedAt = w.CreatedAt
            }).ToList());
        }

        // POST /api/admin/chat/banned-words  (ManageBannedWords)
        [HttpPost("banned-words")]
        public async Task<ActionResult<BannedWordResponseDto>> AddBannedWord(BannedWordCreateDto dto)
        {
            if (!await _permissions.HasPermissionAsync(User, AdminPermission.ManageBannedWords)) return Forbid();

            var word = dto.Word.Trim();
            if (string.IsNullOrEmpty(word)) return BadRequest(new { message = "Word can't be empty." });

            if (await _db.BannedWords.AnyAsync(w => w.Word.ToLower() == word.ToLower()))
                return Conflict(new { message = "That word/phrase is already on the list." });

            var entity = new BannedWord { Word = word, AddedByAdminId = User.GetAdminId(), CreatedAt = DateTime.UtcNow };
            _db.BannedWords.Add(entity);
            await _db.SaveChangesAsync();

            return Ok(new BannedWordResponseDto { Id = entity.Id, Word = entity.Word, AddedByAdminName = "You", CreatedAt = entity.CreatedAt });
        }

        // DELETE /api/admin/chat/banned-words/{id}  (ManageBannedWords)
        [HttpDelete("banned-words/{id:guid}")]
        public async Task<IActionResult> RemoveBannedWord(Guid id)
        {
            if (!await _permissions.HasPermissionAsync(User, AdminPermission.ManageBannedWords)) return Forbid();

            var word = await _db.BannedWords.FindAsync(id);
            if (word == null) return NotFound();

            _db.BannedWords.Remove(word);
            await _db.SaveChangesAsync();
            return NoContent();
        }

        // POST /api/admin/chat/sync-rooms  (Super Admin only) -- backfills a ChatRoom for any Exam
        // that doesn't have one yet (exams created before this feature existed).
        [HttpPost("sync-rooms")]
        [Authorize(Roles = "SuperAdmin")]
        public async Task<IActionResult> SyncRooms()
        {
            var examIdsWithRooms = await _db.ChatRooms.Select(r => r.ExamId).ToListAsync();
            var examsWithoutRooms = await _db.Exams.Where(e => !examIdsWithRooms.Contains(e.Id)).ToListAsync();

            foreach (var exam in examsWithoutRooms)
            {
                _db.ChatRooms.Add(new ChatRoom
                {
                    ExamId = exam.Id,
                    Name = exam.Name,
                    Description = $"Discussion room for {exam.Name} aspirants",
                    IsFeatured = false,
                    CreatedAt = DateTime.UtcNow
                });
            }
            await _db.SaveChangesAsync();

            return Ok(new { message = $"Created {examsWithoutRooms.Count} missing chat room(s)." });
        }
    }
}
