using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using SmartBank.Data;
using SmartBank.DTOs.Chat;
using SmartBank.Entities;
using SmartBank.Services.Interfaces;

namespace SmartBank.Services
{
    public class ChatService : IChatService
    {
        private readonly SmartBankDbContext _context;
        private readonly IWebHostEnvironment _env;

        public ChatService(SmartBankDbContext context, IWebHostEnvironment env)
        {
            _context = context;
            _env = env;
        }

        public async Task<Guid> GetOrCreateConversationForUserAsync(string userId)
        {
            if (string.IsNullOrWhiteSpace(userId))
                throw new ArgumentException("User ID is required.", nameof(userId));

            var conversation = await _context.ChatConversations
                .FirstOrDefaultAsync(c => c.UserId == userId);

            if (conversation == null)
            {
                conversation = new ChatConversation
                {
                    Id = Guid.NewGuid(),
                    UserId = userId,
                    StartedAt = DateTime.UtcNow
                };

                _context.ChatConversations.Add(conversation);
                await _context.SaveChangesAsync();
            }

            return conversation.Id;
        }

        public async Task<List<ChatConversationDto>> GetAllConversationsForAdminAsync()
        {
            var conversations = await _context.ChatConversations
                .AsNoTracking()
                .OrderByDescending(c => c.LastMessageAt ?? c.StartedAt)
                .ToListAsync();

            var userIds = conversations.Select(c => c.UserId).Distinct().ToList();
            var numericUserIds = userIds
                .Select(id => int.TryParse(id, out var parsed) ? parsed : -1)
                .Where(id => id > 0)
                .ToList();

            var users = await _context.Users
                .AsNoTracking()
                .Include(u => u.Accounts)
                .Where(u => numericUserIds.Contains(u.Id))
                .ToDictionaryAsync(u => u.Id.ToString());

            var activeConnections = await _context.ChatConnections
                .AsNoTracking()
                .Select(conn => conn.UserId)
                .Distinct()
                .ToListAsync();

            var result = new List<ChatConversationDto>();

            foreach (var conv in conversations)
            {
                users.TryGetValue(conv.UserId, out var user);

                var unreadCount = await _context.ChatMessages
                    .Where(m => m.ConversationId == conv.Id && m.SenderRole == "User" && !m.IsRead)
                    .CountAsync();

                var accountNumber = user?.Accounts?.FirstOrDefault()?.AccountNumber;

                result.Add(new ChatConversationDto
                {
                    ConversationId = conv.Id,
                    UserId = conv.UserId,
                    UserName = user?.FullName ?? $"User {conv.UserId}",
                    UserEmail = user?.Email ?? string.Empty,
                    AccountNumber = accountNumber,
                    StartedAt = conv.StartedAt,
                    LastMessageAt = conv.LastMessageAt,
                    LastMessagePreview = conv.LastMessagePreview,
                    UnreadCount = unreadCount,
                    IsOnline = activeConnections.Contains(conv.UserId)
                });
            }

            return result;
        }

        public async Task<ChatConversationDto?> GetConversationForUserAsync(string userId)
        {
            if (string.IsNullOrWhiteSpace(userId))
                return null;

            var convId = await GetOrCreateConversationForUserAsync(userId);
            var conv = await _context.ChatConversations
                .AsNoTracking()
                .FirstOrDefaultAsync(c => c.Id == convId);

            if (conv == null) return null;

            int.TryParse(userId, out var numericId);
            var user = await _context.Users
                .AsNoTracking()
                .Include(u => u.Accounts)
                .FirstOrDefaultAsync(u => u.Id == numericId);

            var unreadCount = await _context.ChatMessages
                .Where(m => m.ConversationId == conv.Id && m.SenderRole == "Admin" && !m.IsRead)
                .CountAsync();

            var isOnline = await IsUserOnlineAsync(userId);

            return new ChatConversationDto
            {
                ConversationId = conv.Id,
                UserId = conv.UserId,
                UserName = user?.FullName ?? "Support Client",
                UserEmail = user?.Email ?? string.Empty,
                AccountNumber = user?.Accounts?.FirstOrDefault()?.AccountNumber,
                StartedAt = conv.StartedAt,
                LastMessageAt = conv.LastMessageAt,
                LastMessagePreview = conv.LastMessagePreview,
                UnreadCount = unreadCount,
                IsOnline = isOnline
            };
        }

        public async Task<List<ChatMessageDto>> GetMessagesAsync(Guid conversationId, int page = 1, int pageSize = 50)
        {
            page = Math.Max(1, page);
            pageSize = Math.Clamp(pageSize, 1, 100);

            var conversation = await _context.ChatConversations
                .AsNoTracking()
                .FirstOrDefaultAsync(c => c.Id == conversationId);

            if (conversation == null)
                return new List<ChatMessageDto>();

            var totalCount = await _context.ChatMessages
                .Where(m => m.ConversationId == conversationId)
                .CountAsync();

            var skip = Math.Max(0, totalCount - (page * pageSize));
            var take = pageSize;

            var messages = await _context.ChatMessages
                .AsNoTracking()
                .Where(m => m.ConversationId == conversationId)
                .OrderBy(m => m.SentAt)
                .Skip(skip)
                .Take(take)
                .ToListAsync();

            var senderIds = messages.Select(m => m.SenderId).Distinct().ToList();
            var numericSenderIds = senderIds
                .Select(id => int.TryParse(id, out var parsed) ? parsed : -1)
                .Where(id => id > 0)
                .ToList();

            var userMap = await _context.Users
                .AsNoTracking()
                .Where(u => numericSenderIds.Contains(u.Id))
                .ToDictionaryAsync(u => u.Id.ToString(), u => u.FullName);

            return messages.Select(m => new ChatMessageDto
            {
                Id = m.Id,
                ConversationId = m.ConversationId,
                SenderId = m.SenderId,
                SenderRole = m.SenderRole,
                SenderName = m.SenderRole == "Admin" 
                    ? "SmartBank Support" 
                    : (userMap.TryGetValue(m.SenderId, out var name) ? name : "User"),
                MessageText = m.MessageText,
                AttachmentUrl = m.AttachmentUrl,
                SentAt = m.SentAt,
                IsRead = m.IsRead
            }).ToList();
        }

        public async Task<ChatMessageDto> SaveMessageAsync(Guid conversationId, string senderId, string senderRole, string? messageText, string? attachmentUrl)
        {
            var conversation = await _context.ChatConversations
                .FirstOrDefaultAsync(c => c.Id == conversationId);

            if (conversation == null)
                throw new InvalidOperationException("Chat conversation not found.");

            var message = new ChatMessage
            {
                ConversationId = conversationId,
                SenderId = senderId,
                SenderRole = senderRole,
                MessageText = messageText,
                AttachmentUrl = attachmentUrl,
                SentAt = DateTime.UtcNow,
                IsRead = false
            };

            _context.ChatMessages.Add(message);

            conversation.LastMessageAt = message.SentAt;
            conversation.LastMessagePreview = !string.IsNullOrWhiteSpace(messageText)
                ? messageText
                : (!string.IsNullOrWhiteSpace(attachmentUrl) ? "[Attachment]" : string.Empty);

            await _context.SaveChangesAsync();

            string senderName = "Support";
            if (senderRole == "User")
            {
                if (int.TryParse(senderId, out var uId))
                {
                    var user = await _context.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == uId);
                    if (user != null) senderName = user.FullName;
                }
            }
            else
            {
                senderName = "SmartBank Support";
            }

            return new ChatMessageDto
            {
                Id = message.Id,
                ConversationId = message.ConversationId,
                SenderId = message.SenderId,
                SenderRole = message.SenderRole,
                SenderName = senderName,
                MessageText = message.MessageText,
                AttachmentUrl = message.AttachmentUrl,
                SentAt = message.SentAt,
                IsRead = message.IsRead
            };
        }

        public async Task MarkConversationReadAsync(Guid conversationId, string readerRole)
        {
            var unreadMessages = await _context.ChatMessages
                .Where(m => m.ConversationId == conversationId && m.SenderRole != readerRole && !m.IsRead)
                .ToListAsync();

            if (unreadMessages.Any())
            {
                foreach (var msg in unreadMessages)
                {
                    msg.IsRead = true;
                }
                await _context.SaveChangesAsync();
            }
        }

        public async Task<int> GetUnreadCountForUserAsync(string userId)
        {
            var convId = await _context.ChatConversations
                .Where(c => c.UserId == userId)
                .Select(c => c.Id)
                .FirstOrDefaultAsync();

            if (convId == Guid.Empty) return 0;

            return await _context.ChatMessages
                .Where(m => m.ConversationId == convId && m.SenderRole == "Admin" && !m.IsRead)
                .CountAsync();
        }

        public async Task<int> GetUnreadCountForAdminAsync()
        {
            return await _context.ChatMessages
                .Where(m => m.SenderRole == "User" && !m.IsRead)
                .CountAsync();
        }

        public async Task<string> SaveAttachmentAsync(IFormFile file, string userId)
        {
            if (file == null || file.Length == 0)
                throw new ArgumentException("No file uploaded.", nameof(file));

            if (file.Length > 5 * 1024 * 1024)
                throw new InvalidOperationException("File size exceeds maximum allowed limit of 5 MB.");

            var allowedExtensions = new[] { ".jpg", ".jpeg", ".png", ".pdf" };
            var extension = Path.GetExtension(file.FileName).ToLowerInvariant();

            if (!allowedExtensions.Contains(extension))
                throw new InvalidOperationException("Invalid file type. Only JPG, PNG, and PDF files are allowed.");

            var folderPath = Path.Combine(_env.WebRootPath, "uploads", "chat", userId);
            if (!Directory.Exists(folderPath))
            {
                Directory.CreateDirectory(folderPath);
            }

            var uniqueFileName = $"{Guid.NewGuid()}{extension}";
            var filePath = Path.Combine(folderPath, uniqueFileName);

            using (var stream = new FileStream(filePath, FileMode.Create))
            {
                await file.CopyToAsync(stream);
            }

            return $"/uploads/chat/{userId}/{uniqueFileName}";
        }

        public async Task AddConnectionAsync(string connectionId, string userId, string role)
        {
            var existing = await _context.ChatConnections
                .FirstOrDefaultAsync(c => c.ConnectionId == connectionId);

            if (existing == null)
            {
                _context.ChatConnections.Add(new ChatConnection
                {
                    ConnectionId = connectionId,
                    UserId = userId,
                    Role = role,
                    ConnectedAt = DateTime.UtcNow
                });
                await _context.SaveChangesAsync();
            }
        }

        public async Task RemoveConnectionAsync(string connectionId)
        {
            var connections = await _context.ChatConnections
                .Where(c => c.ConnectionId == connectionId)
                .ToListAsync();

            if (connections.Any())
            {
                _context.ChatConnections.RemoveRange(connections);
                await _context.SaveChangesAsync();
            }
        }

        public async Task<bool> IsUserOnlineAsync(string userId)
        {
            return await _context.ChatConnections
                .AnyAsync(c => c.UserId == userId);
        }

        public async Task<List<string>> GetUserConnectionsAsync(string userId)
        {
            return await _context.ChatConnections
                .Where(c => c.UserId == userId)
                .Select(c => c.ConnectionId)
                .ToListAsync();
        }

        public async Task<bool> CanUserAccessConversationAsync(Guid conversationId, string userId, string role)
        {
            if (role == "Admin") return true;

            var conversation = await _context.ChatConversations
                .AsNoTracking()
                .FirstOrDefaultAsync(c => c.Id == conversationId);

            return conversation != null && conversation.UserId == userId;
        }
    }
}
