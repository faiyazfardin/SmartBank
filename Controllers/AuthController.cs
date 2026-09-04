using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SmartBank.DTOs.Auth;
using SmartBank.DTOs.Common;
using SmartBank.Services.Interfaces;

namespace SmartBank.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class AuthController : ControllerBase
    {
        private readonly IAuthService _authService;

        public AuthController(IAuthService authService)
        {
            _authService = authService;
        }

        [HttpPost("register")]
        public async Task<IActionResult> Register([FromBody] RegisterRequest request)
        {
            if (!ModelState.IsValid)
            {
                var errors = ModelState.Values
                    .SelectMany(v => v.Errors)
                    .Select(e => e.ErrorMessage)
                    .ToList();

                return BadRequest(ApiResponse<RegisterResponse>.FailureResponse("Validation error", errors));
            }

            var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString();
            var (statusCode, response) = await _authService.RegisterAsync(request, ipAddress);

            return StatusCode(statusCode, response);
        }

        [HttpPost("login")]
        public async Task<IActionResult> Login([FromBody] LoginRequest request)
        {
            if (!ModelState.IsValid)
            {
                var errors = ModelState.Values
                    .SelectMany(v => v.Errors)
                    .Select(e => e.ErrorMessage)
                    .ToList();

                return BadRequest(ApiResponse<LoginResponse>.FailureResponse("Validation error", errors));
            }

            var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString();
            var (statusCode, response, retryAfter) = await _authService.LoginAsync(request, ipAddress);

            if (statusCode == 429 && retryAfter.HasValue)
            {
                Response.Headers["Retry-After"] = (retryAfter.Value * 60).ToString();
            }

            return StatusCode(statusCode, response);
        }

        [HttpPost("refresh")]
        public async Task<IActionResult> RefreshToken([FromBody] RefreshTokenRequest request)
        {
            if (!ModelState.IsValid)
            {
                var errors = ModelState.Values
                    .SelectMany(v => v.Errors)
                    .Select(e => e.ErrorMessage)
                    .ToList();

                return BadRequest(ApiResponse<RefreshTokenResponse>.FailureResponse("Validation error", errors));
            }

            var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString();
            var (statusCode, response) = await _authService.RefreshTokenAsync(request, ipAddress);

            return StatusCode(statusCode, response);
        }

        [Authorize]
        [HttpPost("logout")]
        public async Task<IActionResult> Logout([FromBody] RefreshTokenRequest? request)
        {
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                ?? User.FindFirst("sub")?.Value;

            if (int.TryParse(userIdClaim, out var userId))
            {
                var (statusCode, response) = await _authService.LogoutAsync(request?.RefreshToken, userId);
                return StatusCode(statusCode, response);
            }

            return Ok(ApiResponse<bool>.SuccessResponse(true, "Logged out successfully"));
        }

        [Authorize]
        [HttpGet("me")]
        public async Task<IActionResult> GetProfile()
        {
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                ?? User.FindFirst("sub")?.Value;

            if (!int.TryParse(userIdClaim, out var userId))
            {
                return Unauthorized(ApiResponse<LoginResponse>.FailureResponse("Unauthorized", "Invalid token subject"));
            }

            var (statusCode, response) = await _authService.GetCurrentUserProfileAsync(userId);
            return StatusCode(statusCode, response);
        }

        [Authorize]
        [HttpPost("change-password")]
        public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordRequest request)
        {
            if (!ModelState.IsValid)
            {
                var errors = ModelState.Values
                    .SelectMany(v => v.Errors)
                    .Select(e => e.ErrorMessage)
                    .ToList();

                return BadRequest(ApiResponse<bool>.FailureResponse("Validation error", errors));
            }

            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                ?? User.FindFirst("sub")?.Value;

            if (!int.TryParse(userIdClaim, out var userId))
            {
                return Unauthorized(ApiResponse<bool>.FailureResponse("Unauthorized", "Invalid token subject"));
            }

            var (statusCode, response) = await _authService.ChangePasswordAsync(userId, request);
            return StatusCode(statusCode, response);
        }
    }
}
