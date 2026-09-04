using System;
using System.Drawing;
using System.Windows.Forms;
using SmartBank.Client.Security;

namespace SmartBank.Client.Forms
{
    public class UserProfileDialog : Form
    {
        public bool RequestedLogout { get; private set; } = false;
        public bool RequestedPasswordChange { get; private set; } = false;

        public UserProfileDialog()
        {
            InitializeComponents();
        }

        private void InitializeComponents()
        {
            this.Text = "My Account & Profile Details — SmartBank";
            this.ClientSize = new Size(460, 620);
            this.StartPosition = FormStartPosition.CenterParent;
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.BackColor = Color.FromArgb(243, 246, 251);
            this.Font = new Font("Segoe UI", 9F, FontStyle.Regular, GraphicsUnit.Point);

            var s = SessionManager.Instance;

            // 1. TOP PROFILE HEADER BANNER (bKash Style)
            var pnlTop = new Panel
            {
                Dock = DockStyle.Top,
                Height = 150,
                BackColor = Color.FromArgb(15, 23, 42) // Slate 900
            };

            // Avatar Circle
            var initials = "SB";
            if (!string.IsNullOrEmpty(s.FullName))
            {
                var parts = s.FullName.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                initials = parts.Length >= 2 ? $"{parts[0][0]}{parts[1][0]}".ToUpper() : s.FullName.Substring(0, Math.Min(2, s.FullName.Length)).ToUpper();
            }

            var lblAvatar = new Label
            {
                Text = initials,
                Font = new Font("Segoe UI", 18F, FontStyle.Bold),
                ForeColor = Color.White,
                BackColor = Color.FromArgb(37, 99, 235), // Blue 600
                Size = new Size(64, 64),
                Location = new Point(24, 24),
                TextAlign = ContentAlignment.MiddleCenter
            };

            var lblName = new Label
            {
                Text = string.IsNullOrEmpty(s.FullName) ? s.Username : s.FullName,
                Font = new Font("Segoe UI", 14F, FontStyle.Bold),
                ForeColor = Color.White,
                Location = new Point(100, 26),
                AutoSize = true
            };

            var lblUserTag = new Label
            {
                Text = $"@{s.Username} • ID: SB-{s.UserId:D6}",
                Font = new Font("Segoe UI", 9F),
                ForeColor = Color.FromArgb(148, 163, 184),
                Location = new Point(102, 54),
                AutoSize = true
            };

            var lblVerified = new Label
            {
                Text = "● Verified Account • Premier Customer",
                Font = new Font("Segoe UI", 8.5F, FontStyle.Bold),
                ForeColor = Color.FromArgb(52, 211, 153), // Emerald 400
                Location = new Point(102, 78),
                AutoSize = true
            };

            pnlTop.Controls.AddRange(new Control[] { lblAvatar, lblName, lblUserTag, lblVerified });

            // 2. PERSONAL INFO CARD
            var pnlPersonalInfo = CreateSectionCard("PERSONAL INFORMATION", 165, 175);
            AddInfoRow(pnlPersonalInfo, "Email Address", string.IsNullOrEmpty(s.Email) ? $"{s.Username}@smartbank.com" : s.Email, 28);
            AddInfoRow(pnlPersonalInfo, "Phone Number", string.IsNullOrEmpty(s.PhoneNumber) ? "+880 1711-000000" : s.PhoneNumber, 64);
            AddInfoRow(pnlPersonalInfo, "Member Since", s.CreatedAt != DateTime.MinValue ? s.CreatedAt.ToString("MMMM dd, yyyy") : DateTime.Now.ToString("MMMM dd, yyyy"), 100);
            AddInfoRow(pnlPersonalInfo, "Account Status", "🟢 Active & In Good Standing", 136);

            // 3. BANKING & LIMITS CARD
            var pnlBankingInfo = CreateSectionCard("ACCOUNT & LIMITS", 355, 150);
            AddInfoRow(pnlBankingInfo, "Account Number", string.IsNullOrEmpty(s.AccountNumber) ? "N/A" : s.AccountNumber, 28);
            var last4 = s.AccountNumber.Length >= 4 ? s.AccountNumber.Substring(s.AccountNumber.Length - 4) : "8970";
            AddInfoRow(pnlBankingInfo, "Linked Debit Card", $"4532 •••• •••• {last4} (Exp: 09/30)", 64);
            AddInfoRow(pnlBankingInfo, "Daily Transfer Limit", "৳100,000.00 / day", 100);

            // 4. ACTION BUTTONS AT BOTTOM
            var btnCopyAll = new Button
            {
                Text = "📋 Copy Account Details",
                Font = new Font("Segoe UI", 9F, FontStyle.Bold),
                ForeColor = Color.FromArgb(30, 41, 59),
                BackColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Size = new Size(195, 38),
                Location = new Point(24, 525),
                Cursor = Cursors.Hand
            };
            btnCopyAll.FlatAppearance.BorderColor = Color.FromArgb(203, 213, 225);
            btnCopyAll.Click += (evSender, evArgs) =>
            {
                var summary = $"SmartBank Account Details\nName: {s.FullName}\nAccount #: {s.AccountNumber}\nUsername: {s.Username}\nEmail: {s.Email}\nPhone: {s.PhoneNumber}";
                Clipboard.SetText(summary);
                MessageBox.Show("All account details copied to clipboard!", "Copied", MessageBoxButtons.OK, MessageBoxIcon.Information);
            };

            var btnChangePass = new Button
            {
                Text = "🔒 Security Settings",
                Font = new Font("Segoe UI", 9F, FontStyle.Bold),
                ForeColor = Color.FromArgb(37, 99, 235),
                BackColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Size = new Size(195, 38),
                Location = new Point(238, 525),
                Cursor = Cursors.Hand
            };
            btnChangePass.FlatAppearance.BorderColor = Color.FromArgb(191, 219, 254);
            btnChangePass.Click += (evSender, evArgs) =>
            {
                RequestedPasswordChange = true;
                this.Close();
            };

            var btnClose = new Button
            {
                Text = "Close",
                Font = new Font("Segoe UI", 9.5F, FontStyle.Bold),
                ForeColor = Color.White,
                BackColor = Color.FromArgb(15, 23, 42),
                FlatStyle = FlatStyle.Flat,
                Size = new Size(410, 38),
                Location = new Point(24, 570),
                Cursor = Cursors.Hand
            };
            btnClose.FlatAppearance.BorderSize = 0;
            btnClose.Click += (evSender, evArgs) => this.Close();

            this.Controls.AddRange(new Control[] { btnClose, btnChangePass, btnCopyAll, pnlBankingInfo, pnlPersonalInfo, pnlTop });
        }

        private static Panel CreateSectionCard(string headerText, int top, int height)
        {
            var pnl = new Panel
            {
                Location = new Point(24, top),
                Size = new Size(410, height),
                BackColor = Color.White
            };
            pnl.Paint += (s, e) =>
            {
                using var pen = new Pen(Color.FromArgb(226, 232, 240), 1);
                e.Graphics.DrawRectangle(pen, 0, 0, pnl.Width - 1, pnl.Height - 1);
            };

            var lblHeader = new Label
            {
                Text = headerText,
                Font = new Font("Segoe UI", 8F, FontStyle.Bold),
                ForeColor = Color.FromArgb(100, 116, 139),
                Location = new Point(16, 8),
                AutoSize = true
            };
            pnl.Controls.Add(lblHeader);

            return pnl;
        }

        private static void AddInfoRow(Panel parent, string label, string value, int top)
        {
            var lblKey = new Label
            {
                Text = label,
                Font = new Font("Segoe UI", 8.5F),
                ForeColor = Color.FromArgb(100, 116, 139),
                Location = new Point(16, top),
                AutoSize = true
            };

            var lblVal = new Label
            {
                Text = value,
                Font = new Font("Segoe UI", 9F, FontStyle.Bold),
                ForeColor = Color.FromArgb(15, 23, 42),
                Location = new Point(160, top),
                AutoSize = true
            };

            parent.Controls.AddRange(new Control[] { lblKey, lblVal });
        }
    }
}
