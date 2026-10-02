using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using SmartBank.DTOs.Chat;
using SmartBank.Services.Interfaces;

namespace SmartBank.Hubs
{
    [Authorize]
    public class ChatHub : Hub
    {
        private readonly IChatService _chatService;
        private static readonly ConcurrentDictionary<string, List<DateTime>> _userMessageTimestamps = new();

        public ChatHub(IChatService chatService)
        {
            _chatService = chatService;
        }

        private (string UserId, string Role, bool IsAdmin) GetClientContext()
        {
            var userId = Context.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value 
                      ?? Context.User?.FindFirst("sub")?.Value 
                      ?? string.Empty;

            var isAdmin = Context.User?.IsInRole("Admin") == true;
            var role = isAdmin ? "Admin" : "User";

            return (userId, role, isAdmin);
        }

        public override async Task OnConnectedAsync()
        {
            var (userId, role, isAdmin) = GetClientContext();

            if (!string.IsNullOrEmpty(userId))
            {
                await _chatService.AddConnectionAsync(Context.ConnectionId, userId, role);

                if (isAdmin)
                {
                    await Groups.AddToGroupAsync(Context.ConnectionId, "admins");
                    await Clients.Others.SendAsync("UserOnline", userId, "Admin");
                }
                else
                {
                    await Groups.AddToGroupAsync(Context.ConnectionId, $"user_{userId}");
                    await Clients.Group("admins").SendAsync("UserOnline", userId, "User");
                }

                // Send initial unread count to caller
                if (isAdmin)
                {
                    var unreadAdmin = await _chatService.GetUnreadCountForAdminAsync();
                    await Clients.Caller.SendAsync("UnreadCountUpdated", unreadAdmin);
                }
                else
                {
                    var unreadUser = await _chatService.GetUnreadCountForUserAsync(userId);
                    await Clients.Caller.SendAsync("UnreadCountUpdated", unreadUser);
                }
            }

            await base.OnConnectedAsync();
        }

        public override async Task OnDisconnectedAsync(Exception? exception)
        {
            var (userId, role, isAdmin) = GetClientContext();

            if (!string.IsNullOrEmpty(userId))
            {
                await _chatService.RemoveConnectionAsync(Context.ConnectionId);

                var isStillOnline = await _chatService.IsUserOnlineAsync(userId);
                if (!isStillOnline)
                {
                    if (isAdmin)
                    {
                        await Clients.Others.SendAsync("UserOffline", userId, "Admin");
                    }
                    else
                    {
                        await Clients.Group("admins").SendAsync("UserOffline", userId, "User");
                    }
                }
            }

            await base.OnDisconnectedAsync(exception);
        }

        public async Task SendMessage(Guid conversationId, string messageText, string? attachmentUrl)
        {
            var (userId, role, isAdmin) = GetClientContext();

            if (string.IsNullOrEmpty(userId))
                throw new HubException("User is not authenticated.");

            // Security check: Can user access conversation?
            var canAccess = await _chatService.CanUserAccessConversationAsync(conversationId, userId, role);
            if (!canAccess)
            {
                throw new HubException("Unauthorized access to this conversation.");
            }

            // Simple rate limit check: max 10 messages in 10 seconds per connection/userId
            var now = DateTime.UtcNow;
            var timestamps = _userMessageTimestamps.GetOrAdd(userId, _ => new List<DateTime>());
            lock (timestamps)
            {
                timestamps.RemoveAll(t => (now - t).TotalSeconds > 10);
                if (timestamps.Count >= 10)
                {
                    throw new HubException("Rate limit exceeded. Please wait a few seconds before sending more messages.");
                }
                timestamps.Add(now);
            }

            // Sanitize text to prevent XSS
            string? sanitizedText = null;
            if (!string.IsNullOrWhiteSpace(messageText))
            {
                sanitizedText = WebUtility.HtmlEncode(messageText.Trim());
            }

            if (string.IsNullOrWhiteSpace(sanitizedText) && string.IsNullOrWhiteSpace(attachmentUrl))
            {
                throw new HubException("Cannot send empty message without attachment.");
            }

            var messageDto = await _chatService.SaveMessageAsync(conversationId, userId, role, sanitizedText, attachmentUrl);

            // Broadcast message
            if (isAdmin)
            {
                // Send to user's group AND admins group
                var conv = await _chatService.GetConversationForUserAsync(messageDto.SenderId);
                // We need the target conversation owner user ID
                // Let's resolve the user ID from conversation
                await BroadcastToConversationParticipants(conversationId, messageDto, userId, role);
            }
            else
            {
                // Send to admins group AND sender's group
                await Clients.Group("admins").SendAsync("ReceiveMessage", messageDto);
                await Clients.Group($"user_{userId}").SendAsync("ReceiveMessage", messageDto);
            }

            // Update unread counts
            if (isAdmin)
            {
                // Find user for this conversation
                var targetUserId = await GetUserIdForConversationAsync(conversationId);
                if (!string.IsNullOrEmpty(targetUserId))
                {
                    var userUnread = await _chatService.GetUnreadCountForUserAsync(targetUserId);
                    await Clients.Group($"user_{targetUserId}").SendAsync("UnreadCountUpdated", userUnread);
                }
            }
            else
            {
                var adminUnread = await _chatService.GetUnreadCountForAdminAsync();
                await Clients.Group("admins").SendAsync("UnreadCountUpdated", adminUnread);
            }
        }

        public async Task MarkAsRead(Guid conversationId)
        {
            var (userId, role, isAdmin) = GetClientContext();

            if (string.IsNullOrEmpty(userId)) return;

            var canAccess = await _chatService.CanUserAccessConversationAsync(conversationId, userId, role);
            if (!canAccess) return;

            await _chatService.MarkConversationReadAsync(conversationId, role);

            var targetUserId = await GetUserIdForConversationAsync(conversationId);

            if (isAdmin)
            {
                if (!string.IsNullOrEmpty(targetUserId))
                {
                    await Clients.Group($"user_{targetUserId}").SendAsync("MessageRead", conversationId, "Admin");
                }
                await Clients.Group("admins").SendAsync("MessageRead", conversationId, "Admin");

                var adminUnread = await _chatService.GetUnreadCountForAdminAsync();
                await Clients.Caller.SendAsync("UnreadCountUpdated", adminUnread);
            }
            else
            {
                await Clients.Group("admins").SendAsync("MessageRead", conversationId, "User");
                await Clients.Group($"user_{userId}").SendAsync("MessageRead", conversationId, "User");

                var userUnread = await _chatService.GetUnreadCountForUserAsync(userId);
                await Clients.Caller.SendAsync("UnreadCountUpdated", userUnread);
            }
        }

        public async Task Typing(Guid conversationId)
        {
            var (userId, role, isAdmin) = GetClientContext();

            var canAccess = await _chatService.CanUserAccessConversationAsync(conversationId, userId, role);
            if (!canAccess) return;

            if (isAdmin)
            {
                var targetUserId = await GetUserIdForConversationAsync(conversationId);
                if (!string.IsNullOrEmpty(targetUserId))
                {
                    await Clients.Group($"user_{targetUserId}").SendAsync("UserTyping", conversationId, "Admin");
                }
            }
            else
            {
                await Clients.Group("admins").SendAsync("UserTyping", conversationId, "User");
            }
        }

        public async Task StopTyping(Guid conversationId)
        {
            var (userId, role, isAdmin) = GetClientContext();

            var canAccess = await _chatService.CanUserAccessConversationAsync(conversationId, userId, role);
            if (!canAccess) return;

            if (isAdmin)
            {
                var targetUserId = await GetUserIdForConversationAsync(conversationId);
                if (!string.IsNullOrEmpty(targetUserId))
                {
                    await Clients.Group($"user_{targetUserId}").SendAsync("UserStoppedTyping", conversationId, "Admin");
                }
            }
            else
            {
                await Clients.Group("admins").SendAsync("UserStoppedTyping", conversationId, "User");
            }
        }

        private async Task BroadcastToConversationParticipants(Guid conversationId, ChatMessageDto messageDto, string senderId, string senderRole)
        {
            var targetUserId = await GetUserIdForConversationAsync(conversationId);
            
            if (!string.IsNullOrEmpty(targetUserId))
            {
                await Clients.Group($"user_{targetUserId}").SendAsync("ReceiveMessage", messageDto);
            }
            await Clients.Group("admins").SendAsync("ReceiveMessage", messageDto);
        }

        private async Task<string?> GetUserIdForConversationAsync(Guid conversationId)
        {
            var conv = await _chatService.GetMessagesAsync(conversationId, 1, 1);
            // Query DB directly via service helper or DbContext
            // We can query via ChatService helper
            var convDto = (await _chatService.GetAllConversationsForAdminAsync())
                .FirstOrDefault(c => c.ConversationId == conversationId);
            return convDto?.UserId;
        }
    }
}
