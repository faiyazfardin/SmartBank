using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SmartBank.Entities
{
    [Table("ChatMessages")]
    public class ChatMessage
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int Id { get; set; }

        [Required]
        public Guid ConversationId { get; set; }

        [Required]
        [MaxLength(100)]
        public string SenderId { get; set; } = string.Empty;

        [Required]
        [MaxLength(20)]
        public string SenderRole { get; set; } = "User"; // "User" | "Admin"

        public string? MessageText { get; set; }

        [MaxLength(500)]
        public string? AttachmentUrl { get; set; }

        public DateTime SentAt { get; set; } = DateTime.UtcNow;

        public bool IsRead { get; set; } = false;

        // Navigation property
        [ForeignKey(nameof(ConversationId))]
        public virtual ChatConversation? Conversation { get; set; }
    }
}
