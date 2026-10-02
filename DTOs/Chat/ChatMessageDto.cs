using System;

namespace SmartBank.DTOs.Chat
{
    public class ChatMessageDto
    {
        public int Id { get; set; }
        public Guid ConversationId { get; set; }
        public string SenderId { get; set; } = string.Empty;
        public string SenderRole { get; set; } = string.Empty; // "User" | "Admin"
        public string SenderName { get; set; } = string.Empty;
        public string? MessageText { get; set; }
        public string? AttachmentUrl { get; set; }
        public DateTime SentAt { get; set; }
        public bool IsRead { get; set; }
    }
}
