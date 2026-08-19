using System.ComponentModel.DataAnnotations;
using ScoramAPI.Enums;

namespace ScoramAPI.Models
{
    // One room per Exam, created automatically when the Exam is created (see ExamsController.Create)
    // or backfilled for pre-existing exams via the admin "sync rooms" endpoint. Joining is optional --
    // see ChatRoomMembership for who has actually opted in.
    public class ChatRoom
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        // Nullable -- a room no longer has to belong to an Exam. Exam-linked rooms are still the
        // common case (auto-created whenever an Exam is created, see ExamsController and
        // QuestionBankAdminController), but admins can now also create standalone rooms with no Exam
        // at all (e.g. "Daily Doubt Room", "Current Affairs Room" from the SRS room list) via the new
        // AdminChatController CRUD endpoints.
        public Guid? ExamId { get; set; }
        public Exam? Exam { get; set; }

        [Required, MaxLength(100)]
        public string Name { get; set; } = string.Empty;   // denormalized copy of Exam.Name at creation time, when Exam-linked

        public string? Description { get; set; }

        // ADMIN GROUP SETTINGS -- room's own picture, distinct from Exam.LogoUrl (which an exam-linked
        // room shows instead -- see ChatController/AdminChatController's response mapping, both prefer
        // Exam.LogoUrl when ExamId is set). Only meaningful for standalone rooms; uploaded via
        // AdminChatController's UpdateRoomIcon.
        public string? IconUrl { get; set; }

        // ADMIN GROUP SETTINGS -- "who can send messages": AllMembers (default) or AdminOnly (a
        // broadcast-only announcement room). Enforced in ChatController.SendMessage/ShareQuestion --
        // an admin's own PostNotice/CreatePoll actions are separate endpoints and unaffected either way.
        public ChatRoomPostPermission PostPermission { get; set; } = ChatRoomPostPermission.AllMembers;

        // ADMIN GROUP SETTINGS -- optional "About This Community" fields, admin-settable via
        // AdminChatController.UpdateRoom. Both null until an admin sets them -- the info panel
        // simply omits a field that's null rather than showing a fabricated default.
        [MaxLength(50)]
        public string? Language { get; set; }
        public string? Rules { get; set; }

        // GROUP CHAT FIX -- controls whether this room appears in a student's default room list
        // (ChatController.ListRooms with no ?search=) or only when they search for it by name.
        // Exam-linked rooms are created in bulk (every Question Bank exam gets one automatically) and
        // default to false so that doesn't flood the room list -- a student finds a specific exam's
        // room by searching for it. Rooms an admin deliberately creates through the new "Create Group"
        // panel default to true, since curating what's prominently visible is the whole point of that
        // feature. Either way, a room a student has already joined always shows regardless of this flag.
        public bool IsFeatured { get; set; } = true;

        // Admin-controlled kill switch (ToggleChatLock permission) -- when true, no one (except the
        // moderation actions themselves) can post new messages, but history remains visible.
        public bool IsChatDisabled { get; set; } = false;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public ICollection<ChatMessage> Messages { get; set; } = new List<ChatMessage>();
        public ICollection<ChatRoomMembership> Memberships { get; set; } = new List<ChatRoomMembership>();
        public ICollection<ChatPoll> Polls { get; set; } = new List<ChatPoll>();
    }

    // Explicit, permanent membership record -- distinct from SignalR's transient per-connection
    // Groups.AddToGroupAsync, which only tracks "currently connected right now". A student can be a
    // member (IsBanned = false) without being connected at all; a banned student can never rejoin
    // themselves (IsBanned = true) -- only an admin re-adding them lifts it.
    public class ChatRoomMembership
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        public Guid ChatRoomId { get; set; }
        public ChatRoom? ChatRoom { get; set; }

        public Guid UserId { get; set; }
        public User? User { get; set; }

        public DateTime JoinedAt { get; set; } = DateTime.UtcNow;

        public bool IsBanned { get; set; } = false;
        public DateTime? BannedAt { get; set; }
        public Guid? BannedByAdminId { get; set; }
        public Admin? BannedByAdmin { get; set; }
    }

    public class ChatMessage
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        public Guid ChatRoomId { get; set; }
        public ChatRoom? ChatRoom { get; set; }

        // Exactly one of UserId/SenderAdminId is set: a student's Text/Image/Document message sets
        // UserId; an admin's Notice (or the message a Poll is attached to) sets SenderAdminId instead.
        public Guid? UserId { get; set; }
        public User? User { get; set; }

        public Guid? SenderAdminId { get; set; }
        public Admin? SenderAdmin { get; set; }

        public ChatMessageType MessageType { get; set; } = ChatMessageType.Text;

        // Text content for Text/Notice messages; optional caption for Image/Document messages.
        public string? MessageText { get; set; }

        // Set for Image/Document messages -- see FileStorageService.
        public string? AttachmentUrl { get; set; }

        // Set only when MessageType == Poll, linking this message bubble to its poll.
        public Guid? PollId { get; set; }
        public ChatPoll? Poll { get; set; }

        // GROUP CHAT -- set only when MessageType == QuestionShare: a student re-sharing a Scoram
        // Question Bank question into the room (see ChatController.ShareQuestion). MessageText, for
        // this type, holds a short snapshot of the question text at share time so the message still
        // reads sensibly even if the question is later edited or removed -- ShareQuestionExamName
        // gets the same "resilient snapshot" treatment.
        public Guid? SharedQuestionBankQuestionId { get; set; }
        public QuestionBankQuestion? SharedQuestionBankQuestion { get; set; }
        [MaxLength(100)]
        public string? SharedQuestionExamName { get; set; }

        public bool IsDeleted { get; set; } = false;

        // Denormalized convenience flag (fast "has this got any reports" check without a join) --
        // the actual report records/reasons live in ChatReport.
        public bool IsReported { get; set; } = false;

        public DateTime SentAt { get; set; } = DateTime.UtcNow;

        public ICollection<ChatMessageMention> Mentions { get; set; } = new List<ChatMessageMention>();
        public ICollection<ChatReport> Reports { get; set; } = new List<ChatReport>();
    }

    // One row per @username mentioned in a message -- powers the "notify only on mentions" real-time
    // rule and lets a student see their unread mentions even if they were offline when it happened.
    public class ChatMessageMention
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        public Guid ChatMessageId { get; set; }
        public ChatMessage? ChatMessage { get; set; }

        public Guid MentionedUserId { get; set; }
        public User? MentionedUser { get; set; }

        public bool IsRead { get; set; } = false;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }

    public class ChatReport
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        public Guid ChatMessageId { get; set; }
        public ChatMessage? ChatMessage { get; set; }

        public Guid ReportedByUserId { get; set; }
        public User? ReportedByUser { get; set; }

        [Required, MaxLength(500)]
        public string Reason { get; set; } = string.Empty;

        public ChatReportStatus Status { get; set; } = ChatReportStatus.Pending;

        public Guid? ResolvedByAdminId { get; set; }
        public Admin? ResolvedByAdmin { get; set; }
        public DateTime? ResolvedAt { get; set; }
        public string? ResolutionNote { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }

    public class ChatPoll
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        public Guid ChatRoomId { get; set; }
        public ChatRoom? ChatRoom { get; set; }

        public Guid CreatedByAdminId { get; set; }
        public Admin? CreatedByAdmin { get; set; }

        [Required, MaxLength(300)]
        public string Question { get; set; } = string.Empty;

        public bool AllowMultipleChoices { get; set; } = false;

        public bool IsClosed { get; set; } = false;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? ClosedAt { get; set; }

        public ICollection<ChatPollOption> Options { get; set; } = new List<ChatPollOption>();
    }

    public class ChatPollOption
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        public Guid ChatPollId { get; set; }
        public ChatPoll? ChatPoll { get; set; }

        [Required, MaxLength(200)]
        public string OptionText { get; set; } = string.Empty;

        public int DisplayOrder { get; set; }

        public ICollection<ChatPollVote> Votes { get; set; } = new List<ChatPollVote>();
    }

    public class ChatPollVote
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        public Guid ChatPollOptionId { get; set; }
        public ChatPollOption? ChatPollOption { get; set; }

        public Guid UserId { get; set; }
        public User? User { get; set; }

        public DateTime VotedAt { get; set; } = DateTime.UtcNow;
    }

    // Admin-managed list of restricted words/phrases (ManageBannedWords permission). Checked against
    // every message's text before it's allowed to send -- see ChatMessagesController.
    public class BannedWord
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        [Required, MaxLength(200)]
        public string Word { get; set; } = string.Empty;

        public Guid AddedByAdminId { get; set; }
        public Admin? AddedByAdmin { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
