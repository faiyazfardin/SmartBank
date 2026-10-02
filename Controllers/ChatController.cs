using System;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using SmartBank.Services.Interfaces;

namespace SmartBank.Controllers
{
    [Authorize]
    public class ChatController : Controller
    {
        private readonly IChatService _chatService;

        public ChatController(IChatService chatService)
        {
            _chatService = chatService;
        }

        private string GetCurrentUserId()
        {
            return User.FindFirst(ClaimTypes.NameIdentifier)?.Value 
                ?? User.FindFirst("sub")?.Value 
                ?? string.Empty;
        }

        [HttpPost]
        [Route("Chat/UploadAttachment")]
        public async Task<IActionResult> UploadAttachment(IFormFile file)
        {
            var userId = GetCurrentUserId();
            if (string.IsNullOrEmpty(userId))
                return Unauthorized(new { success = false, message = "User is not authenticated." });

            try
            {
                var fileUrl = await _chatService.SaveAttachmentAsync(file, userId);
                return Json(new { success = true, url = fileUrl });
            }
            catch (Exception ex)
            {
                return BadRequest(new { success = false, message = ex.Message });
            }
        }

        [HttpGet]
        [Route("Chat/UserConversation")]
        public async Task<IActionResult> GetUserConversation()
        {
            var userId = GetCurrentUserId();
            if (string.IsNullOrEmpty(userId))
                return Unauthorized();

            var conversation = await _chatService.GetConversationForUserAsync(userId);
            return Json(new { success = true, data = conversation });
        }

        [HttpGet]
        [Route("Chat/Messages")]
        public async Task<IActionResult> GetMessages(Guid conversationId, int page = 1, int pageSize = 50)
        {
            var userId = GetCurrentUserId();
            var isUserAdmin = User.IsInRole("Admin");
            var role = isUserAdmin ? "Admin" : "User";

            var canAccess = await _chatService.CanUserAccessConversationAsync(conversationId, userId, role);
            if (!canAccess)
                return Forbid();

            var messages = await _chatService.GetMessagesAsync(conversationId, page, pageSize);
            return Json(new { success = true, data = messages });
        }

        [HttpGet]
        [Authorize(Roles = "Admin")]
        [Route("Chat/AdminConversations")]
        public async Task<IActionResult> GetAdminConversations()
        {
            var conversations = await _chatService.GetAllConversationsForAdminAsync();
            return Json(new { success = true, data = conversations });
        }

        [HttpGet]
        [Route("Chat/UnreadCount")]
        public async Task<IActionResult> GetUnreadCount()
        {
            var userId = GetCurrentUserId();
            if (string.IsNullOrEmpty(userId))
                return Json(new { success = true, unreadCount = 0 });

            if (User.IsInRole("Admin"))
            {
                var count = await _chatService.GetUnreadCountForAdminAsync();
                return Json(new { success = true, unreadCount = count });
            }
            else
            {
                var count = await _chatService.GetUnreadCountForUserAsync(userId);
                return Json(new { success = true, unreadCount = count });
            }
        }
    }
}
