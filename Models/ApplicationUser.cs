using Microsoft.AspNetCore.Identity;
using Microsoft.CodeAnalysis;
using System.Collections.Generic;

namespace SmartBank.Models
{
    public class ApplicationUser : IdentityUser
    {
        public string FullName { get; set; } = string.Empty;
        public string nID { get; set; } = string.Empty;
        public ICollection<Account> Accounts { get; set; } = new List<Account>();
    }
}