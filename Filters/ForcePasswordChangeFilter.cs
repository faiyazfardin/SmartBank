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
                var controllerName = context.RouteData.Values["controller"]?.ToString() ?? string.Empty;
                var actionName = context.RouteData.Values["action"]?.ToString() ?? string.Empty;

                // Exempt ChangePassword, Logout, and Account/Login GET
                bool isExemptAction = string.Equals(controllerName, "Account", StringComparison.OrdinalIgnoreCase) &&
                    (string.Equals(actionName, "ChangePassword", StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(actionName, "Logout", StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(actionName, "LogoutGet", StringComparison.OrdinalIgnoreCase));

                if (!isExemptAction)
                {
                    var userIdClaim = user.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? user.FindFirst("sub")?.Value;
                    if (int.TryParse(userIdClaim, out var userId) && userId > 0)
                    {
                        var dbUser = await _dbContext.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId);
                        if (dbUser != null && dbUser.MustChangePasswordOnNextLogin)
                        {
                            context.Result = new RedirectToActionResult("ChangePassword", "Account", new { forced = "true" });
                            return;
                        }
                    }
                }
            }

            await next();
        }
    }
}
