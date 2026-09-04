using System;

namespace SmartBank.Client.Models.Transactions
{
    public enum TransactionType
    {
        Deposit,
        Withdraw,
        TransferOut,
        TransferIn
    }

    public class TransactionItem
    {
        public int Id { get; set; }
        public int AccountId { get; set; }
        public TransactionType Type { get; set; }
        public decimal Amount { get; set; }
        public DateTime Timestamp { get; set; }
        public int? RelatedAccountId { get; set; }

        public string FormattedDate => Timestamp.ToLocalTime().ToString("yyyy-MM-dd HH:mm");

        public string TypeDescription => Type switch
        {
            TransactionType.Deposit => "🟢 Deposit",
            TransactionType.Withdraw => "🔴 Withdrawal / Bill",
            TransactionType.TransferOut => "🔵 Transfer Sent",
            TransactionType.TransferIn => "🟣 Transfer Received",
            _ => Type.ToString()
        };

        public string FormattedAmount => Type switch
        {
            TransactionType.Deposit or TransactionType.TransferIn => $"+৳{Amount:N2}",
            TransactionType.Withdraw or TransactionType.TransferOut => $"-৳{Amount:N2}",
            _ => $"৳{Amount:N2}"
        };

        public string Status => "✅ Completed";
    }
}
