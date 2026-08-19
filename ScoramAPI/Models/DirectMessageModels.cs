using System.ComponentModel.DataAnnotations;
using ScoramAPI.Enums;

namespace ScoramAPI.Models
{
    // Deliberately separate from ChatRoom/ChatMessage -- those are Exam-linked, admin-moderated
    // public rooms (bans, reports, polls, banned words). A DM between two students needs none of
    // that: just two participants and a message history.
    //
    // One row per unordered pair of users. UserAId is always the lexicographically-smaller Guid of
    // the two (enforced in DirectMessagesController when a conversation is started) so that
    // "A starts a conversation with B" and "B starts a conversation with A" always resolve to the
    // same row instead of creating a duplicate thread -- see the unique index in ScoramDbContext.
    public class DirectConversation
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        public Guid UserAId { get; set; }
        public User? UserA { get; set; }

        public Guid UserBId { get; set; }
        public User? UserB { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        // Denormalized for fast "sort my conversation list by most recent activity" without
        // aggregating the Messages collection on every request.
        public DateTime LastMessageAt { get; set; } = DateTime.UtcNow;

        public ICollection<DirectMessage> Messages { get; set; } = new List<DirectMessage>();
    }

    public class DirectMessage
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        public Guid ConversationId { get; set; }
        public DirectConversation? Conversation { get; set; }

        public Guid SenderId { get; set; }
        public User? Sender { get; set; }

        public DirectMessageType MessageType { get; set; } = DirectMessageType.Text;

        // Text content for Text messages; optional caption for Image/Document/Audio messages.
        public string? MessageText { get; set; }

        // Set for Image/Document/Audio messages -- see FileStorageService.SaveDirectMessageAttachmentAsync.
        public string? AttachmentUrl { get; set; }

        // Audio only -- the client records duration client-side (MediaRecorder doesn't give the
        // server anything better to measure it from) and sends it alongside the file.
        public int? AttachmentDurationSeconds { get; set; }

        // GROUP CHAT / DM -- set only when MessageType == QuestionShare, same "resilient snapshot"
        // pattern as ChatMessage.SharedQuestionBankQuestionId: MessageText holds the question-text
        // snapshot at share time so the message still reads sensibly even if the question is later
        // edited/removed.
        public Guid? SharedQuestionBankQuestionId { get; set; }
        public QuestionBankQuestion? SharedQuestionBankQuestion { get; set; }
        [MaxLength(100)]
        public string? SharedQuestionExamName { get; set; }

        public bool IsRead { get; set; } = false;

        public bool IsDeleted { get; set; } = false;

        public DateTime SentAt { get; set; } = DateTime.UtcNow;
    }
}
