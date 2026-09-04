using Microsoft.AspNetCore.Identity;
using Microsoft.CodeAnalysis;

namespace SmartBank.Models
{
    public class ApplicationUser : IdentityUser
    {
        public string FullName { get; set; } = string.Empty;
        public string nID { get; set; } = string.Empty;
    }
}