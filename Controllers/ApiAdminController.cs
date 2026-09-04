using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SmartBank.Data;
using SmartBank.DTOs.Common;

namespace SmartBank.Controllers
{
    [ApiController]
    [Route("api/admin")]
    [Authorize(Roles = "Admin")]
    public class ApiAdminController : ControllerBase
    {
        private readonly SmartBankDbContext _context;

        public ApiAdminController(SmartBankDbContext context)
        {
            _context = context;
        }

        public class AdminUserDto
        {
            public int Id { get; set; }
            public string FullName { get; set; } = string.Empty;
            public string Email { get; set; } = string.Empty;
            public string Username { get; set; } = string.Empty;
            public string? PhoneNumber { get; set; }
            public string Role { get; set; } = string.Empty;
            public string Status { get; set; } = string.Empty;
            public int FailedLoginCount { get; set; }
            public bool IsLocked { get; set; }
            public string? AccountNumber { get; set; }
            public decimal Balance { get; set; }
            public bool AccountActive { get; set; }
        }

        [HttpGet("users")]
        public async Task<IActionResult> GetAllUsers()
        {
            var now = System.DateTime.UtcNow;
            var users = await _context.Users
                .Include(u => u.Accounts)
                .OrderByDescending(u => u.CreatedAt)
                .Select(u => new AdminUserDto
                {
                    Id = u.Id,
                    FullName = u.FullName,
                    Email = u.Email,
                    Username = u.Username,
                    PhoneNumber = u.PhoneNumber,
                    Role = u.Role,
                    Status = u.Status,
                    FailedLoginCount = u.FailedLoginCount,
                    IsLocked = u.LockedUntil.HasValue && u.LockedUntil.Value > now,
                    AccountNumber = u.Accounts.Select(a => a.AccountNumber).FirstOrDefault(),
                    Balance = u.Accounts.Select(a => a.Balance).FirstOrDefault(),
                    AccountActive = u.Accounts.Select(a => a.IsActive).FirstOrDefault()
                })
                .ToListAsync();

            return Ok(ApiResponse<List<AdminUserDto>>.SuccessResponse(users));
        }

        [HttpPost("toggle-account")]
        public async Task<IActionResult> ToggleAccountStatus([FromQuery] int userId)
        {
            var account = await _context.Accounts.FirstOrDefaultAsync(a => a.UserId == userId);
            if (account == null)
            {
                return NotFound(ApiResponse<bool>.FailureResponse("Account not found for user"));
            }

            account.IsActive = !account.IsActive;
            account.UpdatedAt = System.DateTime.UtcNow;
            await _context.SaveChangesAsync();

            var status = account.IsActive ? "Activated" : "Frozen";
            return Ok(ApiResponse<bool>.SuccessResponse(account.IsActive, $"Account status changed to {status}"));
        }

        [HttpPost("unlock-user")]
        public async Task<IActionResult> UnlockUser([FromQuery] int userId)
        {
            var user = await _context.Users.FindAsync(userId);
            if (user == null)
            {
                return NotFound(ApiResponse<bool>.FailureResponse("User not found"));
            }

            user.FailedLoginCount = 0;
            user.LockedUntil = null;
            user.UpdatedAt = System.DateTime.UtcNow;
            await _context.SaveChangesAsync();

            return Ok(ApiResponse<bool>.SuccessResponse(true, "User account unlocked successfully"));
        }

        public class SuspendUserRequest
        {
            public int UserId { get; set; }
            public int DurationHours { get; set; } // 0 = Indefinite, 24 = 1d, 168 = 7d, 720 = 30d, 2160 = 90d
            public string Reason { get; set; } = string.Empty;
        }

        [HttpPost("suspend-user")]
        public async Task<IActionResult> SuspendUser([FromBody] SuspendUserRequest request)
        {
            var user = await _context.Users.Include(u => u.Accounts).FirstOrDefaultAsync(u => u.Id == request.UserId);
            if (user == null)
            {
                return NotFound(ApiResponse<bool>.FailureResponse("User not found"));
            }

            if (user.Role.Equals("Admin", System.StringComparison.OrdinalIgnoreCase))
            {
                return BadRequest(ApiResponse<bool>.FailureResponse("Cannot suspend an administrator account"));
            }

            user.Status = "Suspended";
            user.LockedUntil = request.DurationHours > 0
                ? System.DateTime.UtcNow.AddHours(request.DurationHours)
                : System.DateTime.UtcNow.AddYears(100); // Indefinite

            foreach (var acc in user.Accounts)
            {
                acc.IsActive = false; // Freeze transactions immediately
            }
            user.UpdatedAt = System.DateTime.UtcNow;
            await _context.SaveChangesAsync();

            var durationText = request.DurationHours switch
            {
                24 => "1 Day (24 Hours)",
                168 => "7 Days (1 Week)",
                720 => "30 Days (1 Month)",
                2160 => "90 Days (3 Months)",
                _ => request.DurationHours > 0 ? $"{request.DurationHours} Hours" : "Indefinite / Permanent"
            };

            return Ok(ApiResponse<bool>.SuccessResponse(true, $"User @{user.Username} suspended for {durationText}. All transactions blocked."));
        }

        [HttpPost("unsuspend-user")]
        public async Task<IActionResult> UnsuspendUser([FromQuery] int userId)
        {
            var user = await _context.Users.Include(u => u.Accounts).FirstOrDefaultAsync(u => u.Id == userId);
            if (user == null)
            {
                return NotFound(ApiResponse<bool>.FailureResponse("User not found"));
            }

            user.Status = "Active";
            user.LockedUntil = null;
            user.FailedLoginCount = 0;
            foreach (var acc in user.Accounts)
            {
                acc.IsActive = true;
            }
            user.UpdatedAt = System.DateTime.UtcNow;
            await _context.SaveChangesAsync();

            return Ok(ApiResponse<bool>.SuccessResponse(true, $"Suspension lifted for @{user.Username}. Active banking access restored."));
        }

        [HttpPost("toggle-suspend")]
        public async Task<IActionResult> ToggleUserSuspension([FromQuery] int userId)
        {
            var user = await _context.Users.Include(u => u.Accounts).FirstOrDefaultAsync(u => u.Id == userId);
            if (user == null)
            {
                return NotFound(ApiResponse<bool>.FailureResponse("User not found"));
            }

            if (user.Role.Equals("Admin", System.StringComparison.OrdinalIgnoreCase))
            {
                return BadRequest(ApiResponse<bool>.FailureResponse("Cannot suspend an administrator account"));
            }

            bool isNowSuspended = !user.Status.Equals("Suspended", System.StringComparison.OrdinalIgnoreCase);
            user.Status = isNowSuspended ? "Suspended" : "Active";
            if (isNowSuspended)
            {
                user.LockedUntil = System.DateTime.UtcNow.AddYears(100); // Indefinite lock
                foreach (var acc in user.Accounts)
                {
                    acc.IsActive = false; // Freeze all associated accounts
                }
            }
            else
            {
                user.FailedLoginCount = 0;
                user.LockedUntil = null;
                foreach (var acc in user.Accounts)
                {
                    acc.IsActive = true;
                }
            }
            user.UpdatedAt = System.DateTime.UtcNow;
            await _context.SaveChangesAsync();

            var statusMsg = isNowSuspended ? "User and associated banking accounts have been SUSPENDED" : "User suspension lifted and account restored to ACTIVE";
            return Ok(ApiResponse<bool>.SuccessResponse(isNowSuspended, statusMsg));
        }
    }
}
