using System;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.EntityFrameworkCore;
using SmartBank.Data;

namespace SmartBank.Filters
{
    public class ForcePasswordChangeFilter : IAsyncActionFilter
    {
        private readonly SmartBankDbContext _dbContext;

        public ForcePasswordChangeFilter(SmartBankDbContext dbContext)
        {
            _dbContext = dbContext;
        }

        public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
        {
            var httpContext = context.HttpContext;
            var user = httpContext.User;

            if (user.Identity != null && user.Identity.IsAuthenticated)
            {
                // Admin accounts only require User ID and Password (bypass forced changes)
                if (user.IsInRole("Admin"))
                {
                    await next();
                    return;
                }

                var controllerName = context.RouteData.Values["controller"]?.ToString() ?? string.Empty;
                var actionName = context.RouteData.Values["action"]?.ToString() ?? string.Empty;

                // Exempt FirstLoginPasswordChange, ChangePassword, Logout, and Account/Login
                bool isExemptAction = string.Equals(controllerName, "Account", StringComparison.OrdinalIgnoreCase) &&
                    (string.Equals(actionName, "FirstLoginPasswordChange", StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(actionName, "ChangePassword", StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(actionName, "Logout", StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(actionName, "LogoutGet", StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(actionName, "Login", StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(actionName, "AccessDenied", StringComparison.OrdinalIgnoreCase));

                if (!isExemptAction)
                {
                    var userIdClaim = user.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? user.FindFirst("sub")?.Value;
                    if (int.TryParse(userIdClaim, out var userId) && userId > 0)
                    {
                        var dbUser = await _dbContext.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId);
                        if (dbUser != null && !string.Equals(dbUser.Role, "Admin", StringComparison.OrdinalIgnoreCase) && (dbUser.IsFirstLogin || dbUser.MustChangePasswordOnNextLogin))
                        {
                            context.Result = new RedirectToActionResult("FirstLoginPasswordChange", "Account", null);
                            return;
                        }
                    }
                }
            }

            await next();
        }
    }
}
