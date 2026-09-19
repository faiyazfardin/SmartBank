using System;
using System.Drawing;
using System.Windows.Forms;
using SmartBank.Client.Exceptions;
using SmartBank.Client.Models;
using SmartBank.Client.Services;
using SmartBank.DTOs.Common;

namespace SmartBank.Client.Forms
{
    public class TransferOtpDialog : Form
    {
        public int TransferRequestId { get; }
        public decimal Amount { get; }
        public string RecipientAccount { get; }
        public string MaskedEmail { get; }
        public string OtpCode { get; private set; } = string.Empty;

        private TextBox txtOtp = null!;
        private Button btnVerify = null!;
        private Button btnResend = null!;
        private Label lblStatus = null!;

        public TransferOtpDialog(int transferRequestId, decimal amount, string recipientAccount, string maskedEmail)
        {
            TransferRequestId = transferRequestId;
            Amount = amount;
            RecipientAccount = recipientAccount;
            MaskedEmail = maskedEmail;

            InitializeComponents();
        }

        private void InitializeComponents()
        {
            this.Text = "Email OTP Verification — SmartBank";
            this.ClientSize = new Size(420, 380);
            this.StartPosition = FormStartPosition.CenterParent;
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.BackColor = Color.White;
            this.Font = new Font("Segoe UI", 9F);

            var pnlHeader = new Panel
            {
                Dock = DockStyle.Top,
                Height = 65,
                BackColor = Color.FromArgb(248, 250, 252)
            };

            var lblTitle = new Label
            {
                Text = "Security Verification Required",
                Font = new Font("Segoe UI", 11F, FontStyle.Bold),
                ForeColor = Color.FromArgb(15, 23, 42),
                Location = new Point(20, 12),
                AutoSize = true
            };

            var lblSub = new Label
            {
                Text = "Enter the 6-digit OTP code sent to your email",
                Font = new Font("Segoe UI", 8.5F),
                ForeColor = Color.FromArgb(100, 116, 139),
                Location = new Point(20, 35),
                AutoSize = true
            };

            pnlHeader.Controls.AddRange(new Control[] { lblTitle, lblSub });

            var pnlSummary = new Panel
            {
                Location = new Point(20, 80),
                Size = new Size(380, 80),
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

            var lblRec = new Label
            {
                Text = $"Recipient: {RecipientAccount}",
                Font = new Font("Segoe UI", 9F),
                ForeColor = Color.FromArgb(51, 65, 85),
                Location = new Point(14, 34),
                AutoSize = true
            };

            var lblEmail = new Label
            {
                Text = $"Sent To: {MaskedEmail}",
                Font = new Font("Segoe UI", 8.5F),
                ForeColor = Color.FromArgb(100, 116, 139),
                Location = new Point(14, 55),
                AutoSize = true
            };

            pnlSummary.Controls.AddRange(new Control[] { lblAmt, lblRec, lblEmail });

            var lblPrompt = new Label
            {
                Text = "Enter 6-Digit Verification Code:",
                Font = new Font("Segoe UI", 9F, FontStyle.Bold),
                ForeColor = Color.FromArgb(30, 41, 59),
                Location = new Point(20, 175),
                AutoSize = true
            };

            txtOtp = new TextBox
            {
                Location = new Point(20, 200),
                Size = new Size(380, 36),
                Font = new Font("Segoe UI", 16F, FontStyle.Bold),
                TextAlign = HorizontalAlignment.Center,
                MaxLength = 6
            };

            lblStatus = new Label
            {
                Text = "Code valid for 5 minutes",
                Font = new Font("Segoe UI", 8F),
                ForeColor = Color.FromArgb(100, 116, 139),
                Location = new Point(20, 245),
                AutoSize = true
            };

            btnVerify = new Button
            {
                Text = "Verify & Complete Transfer",
                DialogResult = DialogResult.OK,
                Location = new Point(20, 275),
                Size = new Size(240, 44),
                BackColor = Color.FromArgb(37, 99, 235),
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 9.5F, FontStyle.Bold),
                FlatStyle = FlatStyle.Flat,
                Cursor = Cursors.Hand
            };
            btnVerify.FlatAppearance.BorderSize = 0;
            btnVerify.Click += (s, e) =>
            {
                if (string.IsNullOrWhiteSpace(txtOtp.Text) || txtOtp.Text.Trim().Length != 6)
                {
                    MessageBox.Show("Please enter the complete 6-digit verification code.", "Invalid Code", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    this.DialogResult = DialogResult.None;
                    return;
                }
                OtpCode = txtOtp.Text.Trim();
            };

            btnResend = new Button
            {
                Text = "Resend OTP",
                Location = new Point(270, 275),
                Size = new Size(130, 44),
                BackColor = Color.FromArgb(241, 245, 249),
                ForeColor = Color.FromArgb(51, 65, 85),
                Font = new Font("Segoe UI", 9F, FontStyle.Bold),
                FlatStyle = FlatStyle.Flat,
                Cursor = Cursors.Hand
            };
            btnResend.FlatAppearance.BorderSize = 0;
            btnResend.Click += async (s, e) =>
            {
                btnResend.Enabled = false;
                try
                {
                    var res = await ApiClient.PostAsync<ApiResponse<object>>($"transfers/{TransferRequestId}/resend-otp", new { });
                    if (res != null && res.Success)
                    {
                        lblStatus.Text = "A new verification code has been dispatched!";
                        lblStatus.ForeColor = Color.DarkGreen;
                    }
                }
                catch (ApiException ex)
                {
                    MessageBox.Show(ex.Message, "Resend Failed", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
                finally
                {
                    btnResend.Enabled = true;
                }
            };

            var btnCancel = new Button
            {
                Text = "Cancel Transfer",
                DialogResult = DialogResult.Cancel,
                Location = new Point(20, 328),
                Size = new Size(380, 32),
                BackColor = Color.Transparent,
                ForeColor = Color.FromArgb(100, 116, 139),
                Font = new Font("Segoe UI", 8.5F),
                FlatStyle = FlatStyle.Flat,
                Cursor = Cursors.Hand
            };
            btnCancel.FlatAppearance.BorderSize = 0;

            this.Controls.AddRange(new Control[]
            {
                pnlHeader, pnlSummary, lblPrompt, txtOtp, lblStatus, btnVerify, btnResend, btnCancel
            });
        }
    }
}
