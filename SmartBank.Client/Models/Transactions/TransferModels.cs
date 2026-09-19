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

    public class ClientOtpChallengeResult
    {
        public Guid ChallengeId { get; set; }
        public Guid TransactionId { get; set; }
        public string TransactionType { get; set; } = string.Empty;
        public decimal Amount { get; set; }
        public string? TargetInfo { get; set; }
        public string MaskedEmail { get; set; } = string.Empty;
        public DateTime IssuedAt { get; set; }
        public DateTime ExpiresAt { get; set; }
        public int CooldownRemainingSeconds { get; set; } = 60;
    }
}
