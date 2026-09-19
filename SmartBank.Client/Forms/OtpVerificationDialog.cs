using System;
using System.Drawing;
using System.Windows.Forms;
using SmartBank.Client.Exceptions;
using SmartBank.Client.Models.Common;
using SmartBank.Client.Services;

namespace SmartBank.Client.Forms
{
    public class OtpVerificationDialog : Form
    {
        public Guid ChallengeId { get; }
        public string TransactionType { get; }
        public decimal Amount { get; }
        public string TargetInfo { get; }
        public string MaskedEmail { get; }

        public string OtpCode { get; private set; } = string.Empty;
        public string TrackingId { get; private set; } = string.Empty;
        public decimal NewBalance { get; private set; }
        public bool IsVerifiedSuccessfully { get; private set; } = false;

        private TextBox txtOtp = null!;
        private Button btnVerify = null!;
        private Button btnResend = null!;
        private Label lblTimer = null!;
        private Label lblStatus = null!;

        private System.Windows.Forms.Timer _expiryTimer = null!;
        private System.Windows.Forms.Timer _cooldownTimer = null!;

        private int _remainingSeconds = 120; // 2 minutes
        private int _resendCooldownSeconds = 60; // 60s cooldown

        public OtpVerificationDialog(Guid challengeId, string transactionType, decimal amount, string targetInfo, string maskedEmail)
        {
            ChallengeId = challengeId;
            TransactionType = transactionType;
            Amount = amount;
            TargetInfo = targetInfo;
            MaskedEmail = maskedEmail;

            InitializeComponents();
            StartTimers();
        }

        private void InitializeComponents()
        {
            this.Text = $"Verify OTP for {TransactionType} — SmartBank";
            this.ClientSize = new Size(440, 420);
            this.StartPosition = FormStartPosition.CenterParent;
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.BackColor = Color.White;
            this.Font = new Font("Segoe UI", 9F);

            var pnlHeader = new Panel
            {
                Dock = DockStyle.Top,
                Height = 70,
                BackColor = Color.FromArgb(248, 250, 252)
            };

            var lblTitle = new Label
            {
                Text = $"Verify OTP for {TransactionType} — ৳{Amount:N2}",
                Font = new Font("Segoe UI", 11F, FontStyle.Bold),
                ForeColor = Color.FromArgb(15, 23, 42),
                Location = new Point(20, 12),
                AutoSize = true
            };

            var lblSub = new Label
            {
                Text = $"Enter 6-digit OTP sent to {MaskedEmail}",
                Font = new Font("Segoe UI", 8.5F),
                ForeColor = Color.FromArgb(100, 116, 139),
                Location = new Point(20, 38),
                AutoSize = true
            };

            pnlHeader.Controls.AddRange(new Control[] { lblTitle, lblSub });

            var pnlSummary = new Panel
            {
                Location = new Point(20, 85),
                Size = new Size(400, 75),
                BackColor = Color.FromArgb(241, 245, 249)
            };

            var lblAmt = new Label
            {
                Text = $"Amount: ৳{Amount:N2}",
                Font = new Font("Segoe UI", 9.5F, FontStyle.Bold),
                ForeColor = Color.FromArgb(37, 99, 235),
                Location = new Point(14, 12),
                AutoSize = true
            };

            var lblDetail = new Label
            {
                Text = string.IsNullOrWhiteSpace(TargetInfo) ? $"Type: {TransactionType}" : TargetInfo,
                Font = new Font("Segoe UI", 8.5F),
                ForeColor = Color.FromArgb(51, 65, 85),
                Location = new Point(14, 38),
                AutoSize = true
            };

            pnlSummary.Controls.AddRange(new Control[] { lblAmt, lblDetail });

            var lblPrompt = new Label
            {
                Text = "Enter 6-Digit OTP Code:",
                Font = new Font("Segoe UI", 9F, FontStyle.Bold),
                ForeColor = Color.FromArgb(30, 41, 59),
                Location = new Point(20, 175),
                AutoSize = true
            };

            txtOtp = new TextBox
            {
                Location = new Point(20, 200),
                Size = new Size(400, 38),
                Font = new Font("Segoe UI", 16F, FontStyle.Bold),
                TextAlign = HorizontalAlignment.Center,
                MaxLength = 6
            };

            lblTimer = new Label
            {
                Text = "⏰ Expires in: 02:00",
                Font = new Font("Segoe UI", 8.5F, FontStyle.Bold),
                ForeColor = Color.FromArgb(217, 119, 6),
                Location = new Point(20, 245),
                AutoSize = true
            };

            lblStatus = new Label
            {
                Text = "OTP is valid for 2 minutes and single-use only.",
                Font = new Font("Segoe UI", 8F),
                ForeColor = Color.FromArgb(100, 116, 139),
                Location = new Point(20, 268),
                AutoSize = true
            };

            btnVerify = new Button
            {
                Text = "Verify & Complete",
                Location = new Point(20, 300),
                Size = new Size(250, 44),
                BackColor = Color.FromArgb(37, 99, 235),
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 9.5F, FontStyle.Bold),
                FlatStyle = FlatStyle.Flat,
                Cursor = Cursors.Hand
            };
            btnVerify.FlatAppearance.BorderSize = 0;
            btnVerify.Click += async (s, e) => await HandleVerifyAsync();

            btnResend = new Button
            {
                Text = "Resend (60s)",
                Location = new Point(280, 300),
                Size = new Size(140, 44),
                BackColor = Color.FromArgb(241, 245, 249),
                ForeColor = Color.FromArgb(100, 116, 139),
                Font = new Font("Segoe UI", 8.5F, FontStyle.Bold),
                FlatStyle = FlatStyle.Flat,
                Enabled = false,
                Cursor = Cursors.Hand
            };
            btnResend.FlatAppearance.BorderSize = 0;
            btnResend.Click += async (s, e) => await HandleResendAsync();

            var btnCancel = new Button
            {
                Text = "Cancel Transaction",
                DialogResult = DialogResult.Cancel,
                Location = new Point(20, 355),
                Size = new Size(400, 32),
                BackColor = Color.Transparent,
                ForeColor = Color.FromArgb(100, 116, 139),
                Font = new Font("Segoe UI", 8.5F),
                FlatStyle = FlatStyle.Flat,
                Cursor = Cursors.Hand
            };
            btnCancel.FlatAppearance.BorderSize = 0;

            this.Controls.AddRange(new Control[]
            {
                pnlHeader, pnlSummary, lblPrompt, txtOtp, lblTimer, lblStatus, btnVerify, btnResend, btnCancel
            });

            this.FormClosing += (s, e) => StopTimers();
        }

        private void StartTimers()
        {
            _expiryTimer = new System.Windows.Forms.Timer { Interval = 1000 };
            _expiryTimer.Tick += (s, e) =>
            {
                _remainingSeconds--;
                if (_remainingSeconds <= 0)
                {
                    _expiryTimer.Stop();
                    lblTimer.Text = "⏰ EXPIRED";
                    lblTimer.ForeColor = Color.Red;
                    btnVerify.Enabled = false;
                    MessageBox.Show("OTP expired. Please request a new one.", "OTP Expired", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
                else
                {
                    var mins = _remainingSeconds / 60;
                    var secs = _remainingSeconds % 60;
                    lblTimer.Text = $"⏰ Expires in: {mins:D2}:{secs:D2}";
                }
            };
            _expiryTimer.Start();

            _cooldownTimer = new System.Windows.Forms.Timer { Interval = 1000 };
            _cooldownTimer.Tick += (s, e) =>
            {
                _resendCooldownSeconds--;
                if (_resendCooldownSeconds <= 0)
                {
                    _cooldownTimer.Stop();
                    btnResend.Enabled = true;
                    btnResend.Text = "Resend OTP";
                    btnResend.ForeColor = Color.FromArgb(37, 99, 235);
                }
                else
                {
                    btnResend.Text = $"Resend ({_resendCooldownSeconds}s)";
                }
            };
            _cooldownTimer.Start();
        }

        private void StopTimers()
        {
            _expiryTimer?.Stop();
            _cooldownTimer?.Stop();
        }

        private async System.Threading.Tasks.Task HandleVerifyAsync()
        {
            if (string.IsNullOrWhiteSpace(txtOtp.Text) || txtOtp.Text.Trim().Length != 6)
            {
                MessageBox.Show("Please enter the complete 6-digit verification code.", "Invalid Code", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            btnVerify.Enabled = false;
            OtpCode = txtOtp.Text.Trim();

            try
            {
                var payload = new { challengeId = ChallengeId, otp = OtpCode };
                var res = await ApiClient.PostAsync<ApiResponse<VerifyOtpResultDto>>("transactions/otp/verify", payload);

                if (res != null && res.Success && res.Data != null)
                {
                    IsVerifiedSuccessfully = true;
                    TrackingId = res.Data.TrackingId;
                    NewBalance = res.Data.NewBalance;
                    this.DialogResult = DialogResult.OK;
                    this.Close();
                }
                else
                {
                    MessageBox.Show(res?.Message ?? "Verification failed.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
            catch (ApiException ex)
            {
                MessageBox.Show(ex.Message, "Verification Failed", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            finally
            {
                btnVerify.Enabled = true;
            }
        }

        private async System.Threading.Tasks.Task HandleResendAsync()
        {
            btnResend.Enabled = false;
            try
            {
                var payload = new { challengeId = ChallengeId };
                var res = await ApiClient.PostAsync<ApiResponse<object>>("transactions/otp/resend", payload);

                if (res != null && res.Success)
                {
                    lblStatus.Text = "A fresh 6-digit OTP code has been dispatched!";
                    lblStatus.ForeColor = Color.DarkGreen;

                    // Reset timers
                    _remainingSeconds = 120;
                    _resendCooldownSeconds = 60;
                    btnVerify.Enabled = true;
                    StartTimers();
                }
                else
                {
                    MessageBox.Show(res?.Message ?? "Resend failed.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
            catch (ApiException ex)
            {
                MessageBox.Show(ex.Message, "Resend Failed", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        public class VerifyOtpResultDto
        {
            public string TrackingId { get; set; } = string.Empty;
            public decimal NewBalance { get; set; }
        }
    }
}
