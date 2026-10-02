using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using SmartBank.DTOs.Chat;

namespace SmartBank.Services.Interfaces
{
    public interface IChatService
    {
        Task<Guid> GetOrCreateConversationForUserAsync(string userId);
        Task<List<ChatConversationDto>> GetAllConversationsForAdminAsync();
        Task<ChatConversationDto?> GetConversationForUserAsync(string userId);
        Task<List<ChatMessageDto>> GetMessagesAsync(Guid conversationId, int page = 1, int pageSize = 50);
        Task<ChatMessageDto> SaveMessageAsync(Guid conversationId, string senderId, string senderRole, string? messageText, string? attachmentUrl);
        Task MarkConversationReadAsync(Guid conversationId, string readerRole);
        Task<int> GetUnreadCountForUserAsync(string userId);
        Task<int> GetUnreadCountForAdminAsync();
        Task<string> SaveAttachmentAsync(IFormFile file, string userId);
        Task AddConnectionAsync(string connectionId, string userId, string role);
        Task RemoveConnectionAsync(string connectionId);
        Task<bool> IsUserOnlineAsync(string userId);
        Task<List<string>> GetUserConnectionsAsync(string userId);
        Task<bool> CanUserAccessConversationAsync(Guid conversationId, string userId, string role);
    }
}
