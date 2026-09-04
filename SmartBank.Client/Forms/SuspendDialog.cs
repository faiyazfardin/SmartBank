using System;
using System.Drawing;
using System.Windows.Forms;

namespace SmartBank.Client.Forms
{
    public class SuspendDialog : Form
    {
        public int SelectedDurationHours { get; private set; } = 24;
        public string Reason { get; private set; } = string.Empty;

        private RadioButton rb1Day = null!;
        private RadioButton rb7Days = null!;
        private RadioButton rb30Days = null!;
        private RadioButton rb90Days = null!;
        private RadioButton rbIndefinite = null!;
        private TextBox txtReason = null!;

        public SuspendDialog(string fullName, string username, string accountNumber, decimal balance)
        {
            InitializeComponents(fullName, username, accountNumber, balance);
        }

        private void InitializeComponents(string fullName, string username, string accountNumber, decimal balance)
        {
            this.Text = "Administrative Account Suspension — SmartBank";
            this.ClientSize = new Size(480, 490);
            this.StartPosition = FormStartPosition.CenterParent;
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.BackColor = Color.White;
            this.Font = new Font("Segoe UI", 9F, FontStyle.Regular, GraphicsUnit.Point);

            // 1. HEADER
            var pnlTop = new Panel
            {
                Dock = DockStyle.Top,
                Height = 65,
                BackColor = Color.FromArgb(15, 23, 42)
            };

            var lblTitle = new Label
            {
                Text = "🚫 Impose Administrative Suspension",
                Font = new Font("Segoe UI", 12F, FontStyle.Bold),
                ForeColor = Color.White,
                Location = new Point(20, 12),
                AutoSize = true
            };

            var lblSub = new Label
            {
                Text = "Select the suspension timeframe to block all transactions and access.",
                Font = new Font("Segoe UI", 8.5F),
                ForeColor = Color.FromArgb(148, 163, 184),
                Location = new Point(22, 36),
                AutoSize = true
            };
            pnlTop.Controls.AddRange(new Control[] { lblTitle, lblSub });

            // 2. TARGET USER SUMMARY
            var pnlUser = new Panel
            {
                Location = new Point(20, 78),
                Size = new Size(440, 56),
                BackColor = Color.FromArgb(248, 250, 252)
            };
            pnlUser.Paint += (s, e) =>
            {
                using var pen = new Pen(Color.FromArgb(226, 232, 240));
                e.Graphics.DrawRectangle(pen, 0, 0, pnlUser.Width - 1, pnlUser.Height - 1);
            };

            var lblUser = new Label
            {
                Text = $"Target: {fullName} (@{username})",
                Font = new Font("Segoe UI", 9.5F, FontStyle.Bold),
                ForeColor = Color.FromArgb(30, 41, 59),
                Location = new Point(12, 8),
                AutoSize = true
            };

            var lblAcc = new Label
            {
                Text = $"Account #: {accountNumber}  |  Current Balance: ৳{balance:N2}",
                Font = new Font("Segoe UI", 8.5F),
                ForeColor = Color.FromArgb(100, 116, 139),
                Location = new Point(12, 30),
                AutoSize = true
            };
            pnlUser.Controls.AddRange(new Control[] { lblUser, lblAcc });

            // 3. PERIOD SELECTION
            var lblSelect = new Label
            {
                Text = "Choose Suspension Duration:",
                Font = new Font("Segoe UI", 9.5F, FontStyle.Bold),
                ForeColor = Color.FromArgb(30, 41, 59),
                Location = new Point(20, 145),
                AutoSize = true
            };

            rb1Day = CreateRadio("24 Hours (1 Day) — Short security hold & cooling period", new Point(24, 172), true);
            rb7Days = CreateRadio("7 Days (1 Week) — Identity verification & KYC review", new Point(24, 200), false);
            rb30Days = CreateRadio("30 Days (1 Month) — Risk investigation & AML audit", new Point(24, 228), false);
            rb90Days = CreateRadio("90 Days (3 Months) — Formal compliance or regulatory freeze", new Point(24, 256), false);
            rbIndefinite = CreateRadio("Indefinite / Permanent — Until manually lifted by Admin", new Point(24, 284), false);

            // 4. REASON MEMO
            var lblReason = new Label
            {
                Text = "Reason / Compliance Memo (Optional):",
                Font = new Font("Segoe UI", 8.5F, FontStyle.Bold),
                ForeColor = Color.FromArgb(71, 85, 105),
                Location = new Point(20, 320),
                AutoSize = true
            };

            txtReason = new TextBox
            {
                Location = new Point(20, 342),
                Size = new Size(440, 26),
                Font = new Font("Segoe UI", 9.5F),
                PlaceholderText = "e.g. Suspicious transaction volume, chargeback dispute..."
            };

            // 5. BUTTONS
            var btnConfirm = new Button
            {
                Text = "🚫 Confirm Suspension",
                Font = new Font("Segoe UI", 10F, FontStyle.Bold),
                ForeColor = Color.White,
                BackColor = Color.FromArgb(225, 29, 72), // Rose 600
                FlatStyle = FlatStyle.Flat,
                Size = new Size(215, 42),
                Location = new Point(20, 415),
                Cursor = Cursors.Hand,
                DialogResult = DialogResult.OK
            };
            btnConfirm.FlatAppearance.BorderSize = 0;
            btnConfirm.Click += (s, e) =>
            {
                if (rb1Day.Checked) SelectedDurationHours = 24;
                else if (rb7Days.Checked) SelectedDurationHours = 168;
                else if (rb30Days.Checked) SelectedDurationHours = 720;
                else if (rb90Days.Checked) SelectedDurationHours = 2160;
                else if (rbIndefinite.Checked) SelectedDurationHours = 0;

                Reason = txtReason.Text.Trim();
            };

            var btnCancel = new Button
            {
                Text = "Cancel",
                Font = new Font("Segoe UI", 10F, FontStyle.Bold),
                ForeColor = Color.FromArgb(71, 85, 105),
                BackColor = Color.FromArgb(241, 245, 249),
                FlatStyle = FlatStyle.Flat,
                Size = new Size(215, 42),
                Location = new Point(245, 415),
                Cursor = Cursors.Hand,
                DialogResult = DialogResult.Cancel
            };
            btnCancel.FlatAppearance.BorderSize = 0;

            this.Controls.AddRange(new Control[]
            {
                pnlTop, pnlUser, lblSelect,
                rb1Day, rb7Days, rb30Days, rb90Days, rbIndefinite,
                lblReason, txtReason,
                btnConfirm, btnCancel
            });
        }

        private static RadioButton CreateRadio(string text, Point location, bool isChecked)
        {
            return new RadioButton
            {
                Text = text,
                Location = location,
                Size = new Size(430, 24),
                Font = new Font("Segoe UI", 9F),
                ForeColor = Color.FromArgb(51, 65, 85),
                Checked = isChecked,
                Cursor = Cursors.Hand
            };
        }
    }
}
