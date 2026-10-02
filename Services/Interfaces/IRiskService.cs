using System.Threading.Tasks;

namespace SmartBank.Services.Interfaces
{
    public class StepUpResult
    {
        public bool StepUpRequired { get; set; }
        public bool StepUpPassed { get; set; }
        public bool TransactionAllowed { get; set; }
        public string Severity { get; set; } = "Normal"; // Normal, Large, VeryLarge
        public string Message { get; set; } = string.Empty;
        public decimal Average30DayAmount { get; set; }
    }

    public interface IRiskService
    {
        Task<(int NewScore, bool IsFrozen)> RecordRiskEventAsync(
            int userId,
            string eventType,
            int points,
            string? metadataJson = null,
            bool isDecay = false);

        Task AdminUnfreezeUserAsync(int userId, string adminUsername);

        Task<int> AdminManualAdjustScoreAsync(int userId, int pointsAdjustment, string reason, string adminUsername);

        Task<StepUpResult> EvaluateTransactionStepUpAsync(
            int userId,
            decimal amount,
            string? providedVaultPassword = null,
            string? recipientInfo = null,
            string? clientIp = null,
            string? deviceHeader = null);
    }
}
