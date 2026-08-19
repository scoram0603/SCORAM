using Microsoft.AspNetCore.Http;

namespace ScoramAPI.DTOs
{
    public class ChatRoomResponseDto
    {
        public Guid Id { get; set; }
        public Guid? ExamId { get; set; }
        public string ExamName { get; set; } = string.Empty;
        public string? ExamLogoUrl { get; set; }
        public string? IconUrl { get; set; }
        public string? Description { get; set; }
        public bool IsFeatured { get; set; }
        public bool IsChatDisabled { get; set; }
        public string PostPermission { get; set; } = string.Empty;
        public string? Language { get; set; }
        public string? Rules { get; set; }
        public int MemberCount { get; set; }
        // PREMIUM UI -- real-time count from IChatPresenceService, same source as the room header's
        // "Online Members" panel. Cheap to include here (O(1) dictionary lookup per room, no DB
        // query) so the room list can show it without a second round-trip per room.
        public int OnlineCount { get; set; }
        public bool IsMember { get; set; }
        public bool IsBanned { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    // GROUP CHAT -- POST /api/admin/chat/rooms (ManageChatRooms)
    public class ChatRoomCreateDto
    {
        public string Name { get; set; } = string.Empty;
        public string? Description { get; set; }
        public bool IsFeatured { get; set; } = true;
    }

    // GROUP CHAT -- PATCH /api/admin/chat/rooms/{id} (ManageChatRooms). Null fields are left
    // unchanged -- this is a partial update, not a full replace. PostPermission, if given, must be
    // "AllMembers" or "AdminOnly".
    public class ChatRoomUpdateDto
    {
        public string? Name { get; set; }
        public string? Description { get; set; }
        public bool? IsFeatured { get; set; }
        public string? PostPermission { get; set; }
        public string? Language { get; set; }
        public string? Rules { get; set; }
    }

    // [FromForm]-bound -- a message is either text, an attachment (image/document), or both
    // (a caption on an attachment). At least one of the two must be present; enforced in the controller.
    public class ChatMessageSendDto
    {
        public string? MessageText { get; set; }
        public IFormFile? Attachment { get; set; }
    }

    // GROUP CHAT -- POST /api/chat/rooms/{id}/share-question
    public class ShareQuestionDto
    {
        public Guid QuestionBankQuestionId { get; set; }
    }

    public class ChatMessageResponseDto
    {
        public Guid Id { get; set; }
        public Guid ChatRoomId { get; set; }

        // "Student" or "Admin" -- tells the frontend which of the Sender* fields to trust.
        public string SenderType { get; set; } = string.Empty;
        public Guid SenderId { get; set; }
        public string SenderName { get; set; } = string.Empty;
        public string? SenderUsername { get; set; }
        // GROUP CHAT FIX -- null for admin senders (Admin has no profile photo concept in this app);
        // the frontend falls back to initials/a badge icon in that case, same as everywhere else.
        public string? SenderPhotoUrl { get; set; }

        public string MessageType { get; set; } = string.Empty;
        public string? MessageText { get; set; }
        public string? AttachmentUrl { get; set; }

        public Guid? PollId { get; set; }
        public ChatPollResponseDto? Poll { get; set; }

        // Set only when MessageType == "QuestionShare". MessageText carries the question-text
        // snapshot for this type (same field a regular text message uses for its content) --
        // QuestionExists is false when the source question has since been deleted, in which case the
        // snapshot text/exam name still render (nothing here depends on the live question row), but
        // the frontend uses this flag to decide whether the card should still be clickable.
        public Guid? SharedQuestionId { get; set; }
        public string? SharedQuestionExamName { get; set; }
        public bool QuestionExists { get; set; }

        public bool IsDeleted { get; set; }
        public bool IsReported { get; set; }
        public List<string> MentionedUsernames { get; set; } = new();

        public DateTime SentAt { get; set; }
    }

    public class ChatReportCreateDto
    {
        public string Reason { get; set; } = string.Empty;
    }

    public class ChatReportResolveDto
    {
        public string Status { get; set; } = string.Empty; // "ActionTaken" | "Dismissed"
        public string? ResolutionNote { get; set; }
        public bool DeleteMessage { get; set; }
    }

    public class ChatReportResponseDto
    {
        public Guid Id { get; set; }
        public Guid ChatMessageId { get; set; }
        public Guid ChatRoomId { get; set; }
        public string RoomName { get; set; } = string.Empty;
        public string? MessageTextPreview { get; set; }
        public string ReportedByUsername { get; set; } = string.Empty;
        public string Reason { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public string? ResolutionNote { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? ResolvedAt { get; set; }
    }

    public class ChatPollCreateDto
    {
        public string Question { get; set; } = string.Empty;
        public List<string> Options { get; set; } = new();
        public bool AllowMultipleChoices { get; set; } = false;
    }

    public class ChatPollVoteDto
    {
        public List<Guid> OptionIds { get; set; } = new();
    }

    public class ChatPollOptionResponseDto
    {
        public Guid Id { get; set; }
        public string OptionText { get; set; } = string.Empty;
        public int VoteCount { get; set; }
        public bool HasCurrentUserVoted { get; set; }
    }

    public class ChatPollResponseDto
    {
        public Guid Id { get; set; }
        public string Question { get; set; } = string.Empty;
        public bool AllowMultipleChoices { get; set; }
        public bool IsClosed { get; set; }
        public int TotalVotes { get; set; }
        public List<ChatPollOptionResponseDto> Options { get; set; } = new();
        public DateTime CreatedAt { get; set; }
    }

    public class ChatNoticeCreateDto
    {
        public string MessageText { get; set; } = string.Empty;
    }

    public class BannedWordCreateDto
    {
        public string Word { get; set; } = string.Empty;
    }

    public class BannedWordResponseDto
    {
        public Guid Id { get; set; }
        public string Word { get; set; } = string.Empty;
        public string AddedByAdminName { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
    }

    public class ChatMentionResponseDto
    {
        public Guid Id { get; set; }
        public Guid ChatMessageId { get; set; }
        public Guid ChatRoomId { get; set; }
        public string RoomName { get; set; } = string.Empty;
        public string? MessageTextPreview { get; set; }
        public string SenderName { get; set; } = string.Empty;
        public bool IsRead { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    public class MentionableUserDto
    {
        public Guid Id { get; set; }
        public string Username { get; set; } = string.Empty;
        public string FullName { get; set; } = string.Empty;
        public string? PhotoUrl { get; set; }
    }
}
