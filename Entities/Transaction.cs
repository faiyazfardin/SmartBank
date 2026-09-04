using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SmartBank.Entities
{
    public enum TransactionType
    {
        Deposit,
        Withdraw,
        TransferOut,
        TransferIn
    }

    [Table("Transactions")]
    public class Transaction
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int Id { get; set; }

        [Required]
        public int AccountId { get; set; }

        [ForeignKey("AccountId")]
        public virtual Account? Account { get; set; }

        [Required]
        public TransactionType Type { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal Amount { get; set; }

        public DateTime Timestamp { get; set; } = DateTime.UtcNow;

        public int? RelatedAccountId { get; set; }
    }
}
