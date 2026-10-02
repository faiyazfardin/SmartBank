using System;

namespace SmartBank.DTOs.Chat
{
    public class ChatConversationDto
    {
        public Guid ConversationId { get; set; }
        public string UserId { get; set; } = string.Empty;
        public string UserName { get; set; } = string.Empty;
        public string UserEmail { get; set; } = string.Empty;
        public string? AccountNumber { get; set; }
        public DateTime StartedAt { get; set; }
        public DateTime? LastMessageAt { get; set; }
        public string? LastMessagePreview { get; set; }
        public int UnreadCount { get; set; }
        public bool IsOnline { get; set; }
    }
}
