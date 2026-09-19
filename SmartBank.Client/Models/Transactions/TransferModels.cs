using System;

namespace SmartBank.Client.Models.Transactions
{
    public class ClientInitiateTransferResult
    {
        public int TransferRequestId { get; set; }
        public decimal Amount { get; set; }
        public string RecipientAccountNumber { get; set; } = string.Empty;
        public string MaskedRecipient { get; set; } = string.Empty;
        public string MaskedEmail { get; set; } = string.Empty;
        public DateTime ExpiresAt { get; set; }
    }

    public class ClientCompleteTransferResult
    {
        public int TransferRequestId { get; set; }
        public decimal Amount { get; set; }
        public decimal SenderNewBalance { get; set; }
        public string RecipientAccountNumber { get; set; } = string.Empty;
        public string TransactionId { get; set; } = string.Empty;
        public DateTime CompletedAt { get; set; }
    }
}
