using System.Collections.Generic;

namespace SmartBank.Models
{
    public class ApplicationUser
    {
        public string Id { get; set; } = string.Empty;
        public string UserName { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string FullName { get; set; } = string.Empty;
        public string nID { get; set; } = string.Empty;
        public ICollection<Account> Accounts { get; set; } = new List<Account>();
    }
}