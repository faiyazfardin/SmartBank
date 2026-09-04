using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SmartBank.Models
{
    public enum TransactionType
    {
        Deposit,
        Withdraw,
        TransferOut,
        TransferIn
    }

    public class Transaction
    {
        public int Id { get; set; }

        [Required]
        public int AccountId { get; set; }

        [ForeignKey("AccountId")]
        public Account? Account { get; set; }

        [Required]
        public TransactionType Type { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal Amount { get; set; }

        public DateTime Timestamp { get; set; } = DateTime.Now;

        // Only used for transfers — which account the money went to/came from
        public int? RelatedAccountId { get; set; }
    }
}