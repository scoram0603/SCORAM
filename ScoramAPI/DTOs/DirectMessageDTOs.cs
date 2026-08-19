using Microsoft.AspNetCore.Http;

namespace ScoramAPI.DTOs
{
    public class UserSearchResultDto
    {
        public Guid Id { get; set; }
        public string Username { get; set; } = string.Empty;
        public string FullName { get; set; } = string.Empty;
        public string? PhotoUrl { get; set; }
    }

    public class ConversationSummaryDto
    {
        public Guid Id { get; set; }

        // The *other* participant -- never the current user, so the frontend can render this
        // directly as "who am I talking to" without any client-side filtering.
        public Guid OtherUserId { get; set; }
        public string OtherUsername { get; set; } = string.Empty;
        public string OtherFullName { get; set; } = string.Empty;
        public string? OtherPhotoUrl { get; set; }

        public string? LastMessagePreview { get; set; }
        public string? LastMessageType { get; set; }
        public DateTime LastMessageAt { get; set; }
        public int UnreadCount { get; set; }
    }

    // [FromForm]-bound -- a message is either text, an attachment (image/document/audio), or both
    // (a caption on an attachment). At least one of the two must be present; enforced in the controller.
    public class DirectMessageSendDto
    {
        public string? MessageText { get; set; }
        public IFormFile? Attachment { get; set; }

        // Only meaningful when Attachment is a voice note -- the client measures this itself via
        // MediaRecorder, the server has no reliable way to probe an audio file's duration.
        public int? AttachmentDurationSeconds { get; set; }
    }

    public class DirectMessageResponseDto
    {
        public Guid Id { get; set; }
        public Guid ConversationId { get; set; }

        public Guid SenderId { get; set; }
        public string SenderUsername { get; set; } = string.Empty;
        public string SenderFullName { get; set; } = string.Empty;

        public string MessageType { get; set; } = string.Empty;
        public string? MessageText { get; set; }
        public string? AttachmentUrl { get; set; }
        public int? AttachmentDurationSeconds { get; set; }

        // Set only when MessageType == "QuestionShare" -- see ChatDTOs.ChatMessageResponseDto's
        // matching fields, same shape/reasoning.
        public Guid? SharedQuestionId { get; set; }
        public string? SharedQuestionExamName { get; set; }
        public bool QuestionExists { get; set; }

        public bool IsRead { get; set; }
        public bool IsDeleted { get; set; }

        public DateTime SentAt { get; set; }
    }

    // GROUP CHAT / DM -- POST /api/directmessages/conversations/{id}/share-question
    public class ShareQuestionToDmDto
    {
        public Guid QuestionBankQuestionId { get; set; }
    }

    public class StartConversationDto
    {
        public Guid OtherUserId { get; set; }
    }
}
