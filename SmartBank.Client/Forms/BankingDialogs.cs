using System;
using System.Drawing;
using System.Windows.Forms;

namespace SmartBank.Client.Forms
{
    public class DepositDialog : Form
    {
        public decimal DepositAmount { get; private set; }
        private TextBox txtAmount = null!;

        public DepositDialog()
        {
            this.Text = "Deposit Funds — SmartBank";
            this.ClientSize = new Size(430, 320);
            this.StartPosition = FormStartPosition.CenterParent;
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.BackColor = Color.White;
            this.Font = new Font("Segoe UI", 9F);

            var lblPrompt = new Label { Text = "Select a quick preset amount or enter custom amount:", Location = new Point(24, 20), Size = new Size(380, 20), Font = new Font("Segoe UI", 9F, FontStyle.Bold), ForeColor = Color.FromArgb(30, 41, 59) };

            int[] presets = { 500, 1000, 2500, 5000, 10000, 25000 };
            for (int i = 0; i < presets.Length; i++)
            {
                int val = presets[i];
                var btn = new Button
                {
                    Text = $"৳{val:N0}",
                    Font = new Font("Segoe UI", 9.5F, FontStyle.Bold),
                    Size = new Size(115, 38),
                    Location = new Point(24 + (i % 3) * 130, 50 + (i / 3) * 46),
                    BackColor = Color.FromArgb(241, 245, 249),
                    ForeColor = Color.FromArgb(15, 23, 42),
                    FlatStyle = FlatStyle.Flat,
                    Cursor = Cursors.Hand
                };
                btn.FlatAppearance.BorderColor = Color.FromArgb(203, 213, 225);
                btn.Click += (s, e) => txtAmount.Text = val.ToString();
                this.Controls.Add(btn);
            }

            var lblCustom = new Label { Text = "Custom Amount (৳):", Location = new Point(24, 160), AutoSize = true, Font = new Font("Segoe UI", 9F, FontStyle.Bold) };
            txtAmount = new TextBox { Location = new Point(24, 185), Size = new Size(375, 30), Font = new Font("Segoe UI", 12F) };

            var btnSubmit = new Button { Text = "Confirm Deposit", DialogResult = DialogResult.OK, Location = new Point(24, 245), Size = new Size(180, 44), BackColor = Color.FromArgb(16, 185, 129), ForeColor = Color.White, Font = new Font("Segoe UI", 10F, FontStyle.Bold), FlatStyle = FlatStyle.Flat, Cursor = Cursors.Hand };
            btnSubmit.FlatAppearance.BorderSize = 0;
            btnSubmit.Click += (s, e) =>
            {
                if (decimal.TryParse(txtAmount.Text, out var val) && val > 0)
                {
                    DepositAmount = val;
                }
                else
                {
                    MessageBox.Show("Please enter a valid deposit amount greater than ৳0.", "Invalid Amount", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    this.DialogResult = DialogResult.None;
                }
            };

            var btnCancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, Location = new Point(220, 245), Size = new Size(179, 44), BackColor = Color.FromArgb(241, 245, 249), ForeColor = Color.FromArgb(71, 85, 105), Font = new Font("Segoe UI", 10F, FontStyle.Bold), FlatStyle = FlatStyle.Flat, Cursor = Cursors.Hand };
            btnCancel.FlatAppearance.BorderSize = 0;

            this.Controls.AddRange(new Control[] { lblPrompt, lblCustom, txtAmount, btnSubmit, btnCancel });
        }
    }

    public class WithdrawDialog : Form
    {
        public decimal WithdrawAmount { get; private set; }
        private TextBox txtAmount = null!;

        public WithdrawDialog()
        {
            this.Text = "ATM Cash Withdrawal — SmartBank";
            this.ClientSize = new Size(430, 320);
            this.StartPosition = FormStartPosition.CenterParent;
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.BackColor = Color.White;
            this.Font = new Font("Segoe UI", 9F);

            var lblPrompt = new Label { Text = "Select quick ATM cash withdrawal amount:", Location = new Point(24, 20), Size = new Size(380, 20), Font = new Font("Segoe UI", 9F, FontStyle.Bold), ForeColor = Color.FromArgb(30, 41, 59) };

            int[] presets = { 500, 1000, 2000, 5000, 10000, 20000 };
            for (int i = 0; i < presets.Length; i++)
            {
                int val = presets[i];
                var btn = new Button
                {
                    Text = $"৳{val:N0}",
                    Font = new Font("Segoe UI", 9.5F, FontStyle.Bold),
                    Size = new Size(115, 38),
                    Location = new Point(24 + (i % 3) * 130, 50 + (i / 3) * 46),
                    BackColor = Color.FromArgb(241, 245, 249),
                    ForeColor = Color.FromArgb(15, 23, 42),
                    FlatStyle = FlatStyle.Flat,
                    Cursor = Cursors.Hand
                };
                btn.FlatAppearance.BorderColor = Color.FromArgb(203, 213, 225);
                btn.Click += (s, e) => txtAmount.Text = val.ToString();
                this.Controls.Add(btn);
            }

            var lblCustom = new Label { Text = "Custom Withdrawal Amount (৳):", Location = new Point(24, 160), AutoSize = true, Font = new Font("Segoe UI", 9F, FontStyle.Bold) };
            txtAmount = new TextBox { Location = new Point(24, 185), Size = new Size(375, 30), Font = new Font("Segoe UI", 12F) };

            var btnSubmit = new Button { Text = "Confirm Withdrawal", DialogResult = DialogResult.OK, Location = new Point(24, 245), Size = new Size(180, 44), BackColor = Color.FromArgb(245, 158, 11), ForeColor = Color.White, Font = new Font("Segoe UI", 10F, FontStyle.Bold), FlatStyle = FlatStyle.Flat, Cursor = Cursors.Hand };
            btnSubmit.FlatAppearance.BorderSize = 0;
            btnSubmit.Click += (s, e) =>
            {
                if (decimal.TryParse(txtAmount.Text, out var val) && val > 0)
                {
                    WithdrawAmount = val;
                }
                else
                {
                    MessageBox.Show("Please enter a valid withdrawal amount greater than ৳0.", "Invalid Amount", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    this.DialogResult = DialogResult.None;
                }
            };

            var btnCancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, Location = new Point(220, 245), Size = new Size(179, 44), BackColor = Color.FromArgb(241, 245, 249), ForeColor = Color.FromArgb(71, 85, 105), Font = new Font("Segoe UI", 10F, FontStyle.Bold), FlatStyle = FlatStyle.Flat, Cursor = Cursors.Hand };
            btnCancel.FlatAppearance.BorderSize = 0;

            this.Controls.AddRange(new Control[] { lblPrompt, lblCustom, txtAmount, btnSubmit, btnCancel });
        }
    }

    public class TransferDialog : Form
    {
        public string RecipientAccount { get; private set; } = string.Empty;
        public decimal TransferAmount { get; private set; }
        public string Note { get; private set; } = string.Empty;

        private TextBox txtRecipient = null!;
        private TextBox txtAmount = null!;
        private TextBox txtNote = null!;

        public TransferDialog()
        {
            this.Text = "Transfer Funds — SmartBank";
            this.ClientSize = new Size(430, 360);
            this.StartPosition = FormStartPosition.CenterParent;
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.BackColor = Color.White;
            this.Font = new Font("Segoe UI", 9F);

            var lblRec = new Label { Text = "Recipient 12-Digit Account Number:", Location = new Point(24, 20), AutoSize = true, Font = new Font("Segoe UI", 9F, FontStyle.Bold) };
            txtRecipient = new TextBox { Location = new Point(24, 45), Size = new Size(375, 28), Font = new Font("Segoe UI", 11F) };

            var lblAmt = new Label { Text = "Transfer Amount (৳):", Location = new Point(24, 95), AutoSize = true, Font = new Font("Segoe UI", 9F, FontStyle.Bold) };
            txtAmount = new TextBox { Location = new Point(24, 120), Size = new Size(375, 28), Font = new Font("Segoe UI", 11F) };

            var lblNote = new Label { Text = "Reference / Memo (Optional):", Location = new Point(24, 170), AutoSize = true, Font = new Font("Segoe UI", 9F, FontStyle.Bold) };
            txtNote = new TextBox { Location = new Point(24, 195), Size = new Size(375, 28), Font = new Font("Segoe UI", 10F) };

            var btnSubmit = new Button { Text = "Send Transfer", DialogResult = DialogResult.OK, Location = new Point(24, 270), Size = new Size(180, 44), BackColor = Color.FromArgb(37, 99, 235), ForeColor = Color.White, Font = new Font("Segoe UI", 10F, FontStyle.Bold), FlatStyle = FlatStyle.Flat, Cursor = Cursors.Hand };
            btnSubmit.FlatAppearance.BorderSize = 0;
            btnSubmit.Click += (s, e) =>
            {
                if (string.IsNullOrWhiteSpace(txtRecipient.Text) || txtRecipient.Text.Trim().Length != 12)
                {
                    MessageBox.Show("Please enter a valid 12-digit recipient account number.", "Invalid Account", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    this.DialogResult = DialogResult.None;
                    return;
                }

                if (!decimal.TryParse(txtAmount.Text, out var val) || val <= 0)
                {
                    MessageBox.Show("Please enter a valid transfer amount greater than ৳0.", "Invalid Amount", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    this.DialogResult = DialogResult.None;
                    return;
                }

                RecipientAccount = txtRecipient.Text.Trim();
                TransferAmount = val;
                Note = txtNote.Text.Trim();
            };

            var btnCancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, Location = new Point(220, 270), Size = new Size(179, 44), BackColor = Color.FromArgb(241, 245, 249), ForeColor = Color.FromArgb(71, 85, 105), Font = new Font("Segoe UI", 10F, FontStyle.Bold), FlatStyle = FlatStyle.Flat, Cursor = Cursors.Hand };
            btnCancel.FlatAppearance.BorderSize = 0;

            this.Controls.AddRange(new Control[] { lblRec, txtRecipient, lblAmt, txtAmount, lblNote, txtNote, btnSubmit, btnCancel });
        }
    }

    public class BillPayDialog : Form
    {
        public string BillerName { get; private set; } = string.Empty;
        public string BillType { get; private set; } = string.Empty;
        public string ReferenceNumber { get; private set; } = string.Empty;
        public decimal BillAmount { get; private set; }

        private ComboBox cmbBiller = null!;
        private ComboBox cmbType = null!;
        private TextBox txtRef = null!;
        private TextBox txtAmount = null!;

        public BillPayDialog()
        {
            this.Text = "Pay Utility & Service Bills — SmartBank";
            this.ClientSize = new Size(430, 390);
            this.StartPosition = FormStartPosition.CenterParent;
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.BackColor = Color.White;
            this.Font = new Font("Segoe UI", 9F);

            var lblBiller = new Label { Text = "Select Service Provider / Biller:", Location = new Point(24, 18), AutoSize = true, Font = new Font("Segoe UI", 9F, FontStyle.Bold) };
            cmbBiller = new ComboBox { Location = new Point(24, 42), Size = new Size(375, 28), DropDownStyle = ComboBoxStyle.DropDownList, Font = new Font("Segoe UI", 10F) };
            cmbBiller.Items.AddRange(new object[] { "DESCO / DPDC Electricity", "Dhaka WASA Water", "Link3 / Dot Internet", "Grameenphone / Banglalink Mobile", "Titas Gas Distribution", "Chaldal / Daraz Merchant" });
            cmbBiller.SelectedIndex = 0;

            var lblType = new Label { Text = "Bill Category:", Location = new Point(24, 88), AutoSize = true, Font = new Font("Segoe UI", 9F, FontStyle.Bold) };
            cmbType = new ComboBox { Location = new Point(24, 112), Size = new Size(375, 28), DropDownStyle = ComboBoxStyle.DropDownList, Font = new Font("Segoe UI", 10F) };
            cmbType.Items.AddRange(new object[] { "Electricity", "Water", "Internet & Fiber", "Mobile Recharge", "Gas", "Merchant Payment" });
            cmbType.SelectedIndex = 0;

            var lblRef = new Label { Text = "Invoice / Account Reference #:", Location = new Point(24, 158), AutoSize = true, Font = new Font("Segoe UI", 9F, FontStyle.Bold) };
            txtRef = new TextBox { Location = new Point(24, 182), Size = new Size(375, 28), Font = new Font("Segoe UI", 10F), Text = $"INV-{new Random().Next(100000, 999999)}" };

            var lblAmt = new Label { Text = "Payment Amount (৳):", Location = new Point(24, 228), AutoSize = true, Font = new Font("Segoe UI", 9F, FontStyle.Bold) };
            txtAmount = new TextBox { Location = new Point(24, 252), Size = new Size(375, 28), Font = new Font("Segoe UI", 11F) };

            var btnSubmit = new Button { Text = "Pay Bill Now", DialogResult = DialogResult.OK, Location = new Point(24, 310), Size = new Size(180, 44), BackColor = Color.FromArgb(139, 92, 246), ForeColor = Color.White, Font = new Font("Segoe UI", 10F, FontStyle.Bold), FlatStyle = FlatStyle.Flat, Cursor = Cursors.Hand };
            btnSubmit.FlatAppearance.BorderSize = 0;
            btnSubmit.Click += (s, e) =>
            {
                if (!decimal.TryParse(txtAmount.Text, out var val) || val <= 0)
                {
                    MessageBox.Show("Please enter a valid bill amount greater than ৳0.", "Invalid Amount", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    this.DialogResult = DialogResult.None;
                    return;
                }

                BillerName = cmbBiller.SelectedItem?.ToString() ?? "Utility Provider";
                BillType = cmbType.SelectedItem?.ToString() ?? "Utility";
                ReferenceNumber = txtRef.Text.Trim();
                BillAmount = val;
            };

            var btnCancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, Location = new Point(220, 310), Size = new Size(179, 44), BackColor = Color.FromArgb(241, 245, 249), ForeColor = Color.FromArgb(71, 85, 105), Font = new Font("Segoe UI", 10F, FontStyle.Bold), FlatStyle = FlatStyle.Flat, Cursor = Cursors.Hand };
            btnCancel.FlatAppearance.BorderSize = 0;

            this.Controls.AddRange(new Control[] { lblBiller, cmbBiller, lblType, cmbType, lblRef, txtRef, lblAmt, txtAmount, btnSubmit, btnCancel });
        }
    }

    public class ChangePasswordDialog : Form
    {
        public string CurrentPassword => txtCurrent.Text;
        public string NewPassword => txtNew.Text;
        public string ConfirmPassword => txtConfirm.Text;

        private TextBox txtCurrent = null!;
        private TextBox txtNew = null!;
        private TextBox txtConfirm = null!;

        public ChangePasswordDialog()
        {
            this.Text = "Change Account Password — SmartBank";
            this.ClientSize = new Size(410, 340);
            this.StartPosition = FormStartPosition.CenterParent;
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.BackColor = Color.White;
            this.Font = new Font("Segoe UI", 9F);

            var lblCur = new Label { Text = "Current Password:", Location = new Point(24, 20), AutoSize = true, Font = new Font("Segoe UI", 9F, FontStyle.Bold) };
            txtCurrent = new TextBox { Location = new Point(24, 45), Size = new Size(355, 28), PasswordChar = '●', Font = new Font("Segoe UI", 10F) };

            var lblNew = new Label { Text = "New Password (min 8 chars, mixed case + special):", Location = new Point(24, 90), AutoSize = true, Font = new Font("Segoe UI", 9F, FontStyle.Bold) };
            txtNew = new TextBox { Location = new Point(24, 115), Size = new Size(355, 28), PasswordChar = '●', Font = new Font("Segoe UI", 10F) };

            var lblConf = new Label { Text = "Confirm New Password:", Location = new Point(24, 160), AutoSize = true, Font = new Font("Segoe UI", 9F, FontStyle.Bold) };
            txtConfirm = new TextBox { Location = new Point(24, 185), Size = new Size(355, 28), PasswordChar = '●', Font = new Font("Segoe UI", 10F) };

            var btnSubmit = new Button { Text = "Update Password", DialogResult = DialogResult.OK, Location = new Point(24, 250), Size = new Size(175, 42), BackColor = Color.FromArgb(37, 99, 235), ForeColor = Color.White, Font = new Font("Segoe UI", 9.5F, FontStyle.Bold), FlatStyle = FlatStyle.Flat, Cursor = Cursors.Hand };
            btnSubmit.FlatAppearance.BorderSize = 0;
            btnSubmit.Click += (s, e) =>
            {
                if (string.IsNullOrEmpty(txtCurrent.Text) || string.IsNullOrEmpty(txtNew.Text))
                {
                    MessageBox.Show("Please complete all password fields.", "Warning", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    this.DialogResult = DialogResult.None;
                    return;
                }
                if (txtNew.Text != txtConfirm.Text)
                {
                    MessageBox.Show("New passwords do not match.", "Warning", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    this.DialogResult = DialogResult.None;
                    return;
                }
            };

            var btnCancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, Location = new Point(210, 250), Size = new Size(169, 42), BackColor = Color.FromArgb(241, 245, 249), ForeColor = Color.FromArgb(71, 85, 105), Font = new Font("Segoe UI", 9.5F, FontStyle.Bold), FlatStyle = FlatStyle.Flat, Cursor = Cursors.Hand };
            btnCancel.FlatAppearance.BorderSize = 0;

            this.Controls.AddRange(new Control[] { lblCur, txtCurrent, lblNew, txtNew, lblConf, txtConfirm, btnSubmit, btnCancel });
        }
    }
}
