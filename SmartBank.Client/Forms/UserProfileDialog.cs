using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Threading.Tasks;
using System.Windows.Forms;
using SmartBank.Client.Exceptions;
using SmartBank.Client.Models.Auth;
using SmartBank.Client.Models.Common;
using SmartBank.Client.Security;
using SmartBank.Client.Services;

namespace SmartBank.Client.Forms
{
    public class UserProfileDialog : Form
    {
        public bool RequestedLogout { get; private set; } = false;
        public bool RequestedPasswordChange { get; private set; } = false;
        public bool HasChangesSaved { get; private set; } = false;

        private Label lblAvatar = null!;
        private Label lblName = null!;
        private Label lblUserTag = null!;
        private Label lblVerified = null!;
        private Label lblEmailVal = null!;
        private Label lblPhoneVal = null!;
        private Label lblMemberVal = null!;
        private Label lblStatusVal = null!;

        private Panel pnlPersonalInfo = null!;
        private Panel pnlEditFields = null!;
        private Panel pnlViewFields = null!;

        private TextBox txtEditFullName = null!;
        private TextBox txtEditEmail = null!;
        private TextBox txtEditPhone = null!;
        private Label lblEditError = null!;

        private Button btnEditProfile = null!;
        private Button btnSaveProfile = null!;
        private Button btnCancelEdit = null!;
        private Button btnCopyAll = null!;
        private Button btnChangePass = null!;
        private Button btnClose = null!;

        private bool _isEditMode = false;

        public UserProfileDialog()
        {
            InitializeComponents();
        }

        private void InitializeComponents()
        {
            this.Text = "My Account & Profile Details — SmartBank";
            this.ClientSize = new Size(470, 650);
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
                Height = 140,
                BackColor = Color.FromArgb(15, 23, 42) // Slate 900
            };

            var initials = GetInitials(s.FullName, s.Username);

            lblAvatar = new Label
            {
                Text = initials,
                Font = new Font("Segoe UI", 18F, FontStyle.Bold),
                ForeColor = Color.White,
                BackColor = s.Role.Equals("Admin", StringComparison.OrdinalIgnoreCase) ? Color.FromArgb(225, 29, 72) : Color.FromArgb(37, 99, 235),
                Size = new Size(64, 64),
                Location = new Point(24, 24),
                TextAlign = ContentAlignment.MiddleCenter
            };

            lblName = new Label
            {
                Text = string.IsNullOrEmpty(s.FullName) ? s.Username : s.FullName,
                Font = new Font("Segoe UI", 13.5F, FontStyle.Bold),
                ForeColor = Color.White,
                Location = new Point(100, 24),
                AutoSize = true
            };

            lblUserTag = new Label
            {
                Text = $"@{s.Username} • ID: SB-{s.UserId:D6} • {s.Role}",
                Font = new Font("Segoe UI", 8.5F),
                ForeColor = Color.FromArgb(148, 163, 184),
                Location = new Point(102, 52),
                AutoSize = true
            };

            lblVerified = new Label
            {
                Text = s.Role.Equals("Admin", StringComparison.OrdinalIgnoreCase) ? "🛡️ System Administrator • Full Access" : "● Verified Account • Premier Customer",
                Font = new Font("Segoe UI", 8.5F, FontStyle.Bold),
                ForeColor = s.Role.Equals("Admin", StringComparison.OrdinalIgnoreCase) ? Color.FromArgb(248, 113, 113) : Color.FromArgb(52, 211, 153),
                Location = new Point(102, 76),
                AutoSize = true
            };

            pnlTop.Controls.AddRange(new Control[] { lblAvatar, lblName, lblUserTag, lblVerified });

            // 2. PERSONAL INFO CARD (Switchable between View & Edit mode)
            pnlPersonalInfo = new Panel
            {
                Location = new Point(24, 155),
                Size = new Size(422, 205),
                BackColor = Color.White
            };
            pnlPersonalInfo.Paint += (s1, e1) =>
            {
                using var pen = new Pen(Color.FromArgb(226, 232, 240), 1);
                e1.Graphics.DrawRectangle(pen, 0, 0, pnlPersonalInfo.Width - 1, pnlPersonalInfo.Height - 1);
            };

            var lblPersonalHeader = new Label
            {
                Text = "PERSONAL INFORMATION",
                Font = new Font("Segoe UI", 8F, FontStyle.Bold),
                ForeColor = Color.FromArgb(100, 116, 139),
                Location = new Point(16, 10),
                AutoSize = true
            };

            btnEditProfile = new Button
            {
                Text = "✏️ Edit Info",
                Font = new Font("Segoe UI", 8F, FontStyle.Bold),
                ForeColor = Color.FromArgb(37, 99, 235),
                BackColor = Color.FromArgb(239, 246, 255),
                FlatStyle = FlatStyle.Flat,
                Size = new Size(88, 26),
                Location = new Point(318, 6),
                Cursor = Cursors.Hand
            };
            btnEditProfile.FlatAppearance.BorderColor = Color.FromArgb(191, 219, 254);
            btnEditProfile.Click += (evSender, evArgs) => SetEditMode(true);

            pnlPersonalInfo.Controls.AddRange(new Control[] { lblPersonalHeader, btnEditProfile });

            // View Fields Panel
            pnlViewFields = new Panel
            {
                Location = new Point(0, 36),
                Size = new Size(422, 160),
                BackColor = Color.Transparent
            };

            AddViewRow(pnlViewFields, "Full Name", string.IsNullOrEmpty(s.FullName) ? s.Username : s.FullName, 4, out _);
            AddViewRow(pnlViewFields, "Email Address", string.IsNullOrEmpty(s.Email) ? $"{s.Username}@smartbank.com" : s.Email, 40, out lblEmailVal);
            AddViewRow(pnlViewFields, "Phone Number", string.IsNullOrEmpty(s.PhoneNumber) ? "+880 1711-000000" : s.PhoneNumber, 76, out lblPhoneVal);
            AddViewRow(pnlViewFields, "Member Since", s.CreatedAt != DateTime.MinValue ? s.CreatedAt.ToString("MMMM dd, yyyy") : DateTime.Now.ToString("MMMM dd, yyyy"), 112, out lblMemberVal);
            pnlPersonalInfo.Controls.Add(pnlViewFields);

            // Edit Fields Panel
            pnlEditFields = new Panel
            {
                Location = new Point(0, 34),
                Size = new Size(422, 165),
                BackColor = Color.Transparent,
                Visible = false
            };

            // Full Name input
            var lblEditFn = new Label { Text = "Full Name:", Font = new Font("Segoe UI", 8F, FontStyle.Bold), ForeColor = Color.FromArgb(71, 85, 105), Location = new Point(16, 6), AutoSize = true };
            txtEditFullName = new TextBox { Location = new Point(110, 3), Size = new Size(295, 24), Font = new Font("Segoe UI", 9F) };

            // Email input
            var lblEditEm = new Label { Text = "Email Address:", Font = new Font("Segoe UI", 8F, FontStyle.Bold), ForeColor = Color.FromArgb(71, 85, 105), Location = new Point(16, 38), AutoSize = true };
            txtEditEmail = new TextBox { Location = new Point(110, 35), Size = new Size(295, 24), Font = new Font("Segoe UI", 9F) };

            // Phone input
            var lblEditPh = new Label { Text = "Phone Number:", Font = new Font("Segoe UI", 8F, FontStyle.Bold), ForeColor = Color.FromArgb(71, 85, 105), Location = new Point(16, 70), AutoSize = true };
            txtEditPhone = new TextBox { Location = new Point(110, 67), Size = new Size(295, 24), Font = new Font("Segoe UI", 9F) };

            // Edit error label
            lblEditError = new Label
            {
                Location = new Point(16, 96),
                Size = new Size(390, 24),
                Font = new Font("Segoe UI", 8F, FontStyle.Bold),
                ForeColor = Color.FromArgb(225, 29, 72),
                Visible = false
            };

            // Save & Cancel buttons
            btnCancelEdit = new Button
            {
                Text = "✕ Cancel",
                Font = new Font("Segoe UI", 8.5F, FontStyle.Bold),
                ForeColor = Color.FromArgb(71, 85, 105),
                BackColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Size = new Size(140, 30),
                Location = new Point(110, 124),
                Cursor = Cursors.Hand
            };
            btnCancelEdit.FlatAppearance.BorderColor = Color.FromArgb(203, 213, 225);
            btnCancelEdit.Click += (evSender, evArgs) => SetEditMode(false);

            btnSaveProfile = new Button
            {
                Text = "💾 Save Changes",
                Font = new Font("Segoe UI", 8.5F, FontStyle.Bold),
                ForeColor = Color.White,
                BackColor = Color.FromArgb(37, 99, 235),
                FlatStyle = FlatStyle.Flat,
                Size = new Size(145, 30),
                Location = new Point(260, 124),
                Cursor = Cursors.Hand
            };
            btnSaveProfile.FlatAppearance.BorderSize = 0;
            btnSaveProfile.Click += BtnSaveProfile_Click;

            pnlEditFields.Controls.AddRange(new Control[] { lblEditFn, txtEditFullName, lblEditEm, txtEditEmail, lblEditPh, txtEditPhone, lblEditError, btnCancelEdit, btnSaveProfile });
            pnlPersonalInfo.Controls.Add(pnlEditFields);

            // 3. BANKING & LIMITS CARD
            var pnlBankingInfo = new Panel
            {
                Location = new Point(24, 372),
                Size = new Size(422, 130),
                BackColor = Color.White
            };
            pnlBankingInfo.Paint += (s1, e1) =>
            {
                using var pen = new Pen(Color.FromArgb(226, 232, 240), 1);
                e1.Graphics.DrawRectangle(pen, 0, 0, pnlBankingInfo.Width - 1, pnlBankingInfo.Height - 1);
            };

            var lblBankHeader = new Label
            {
                Text = "ACCOUNT & LIMITS",
                Font = new Font("Segoe UI", 8F, FontStyle.Bold),
                ForeColor = Color.FromArgb(100, 116, 139),
                Location = new Point(16, 10),
                AutoSize = true
            };
            pnlBankingInfo.Controls.Add(lblBankHeader);

            AddViewRow(pnlBankingInfo, "Account Number", string.IsNullOrEmpty(s.AccountNumber) ? "N/A" : s.AccountNumber, 36, out _);
            var last4 = s.AccountNumber.Length >= 4 ? s.AccountNumber.Substring(s.AccountNumber.Length - 4) : "8970";
            AddViewRow(pnlBankingInfo, "Linked Debit Card", $"4532 •••• •••• {last4} (Exp: 09/30)", 68, out _);
            AddViewRow(pnlBankingInfo, "Daily Transfer Limit", "৳100,000.00 / day", 100, out _);

            // 4. ACTION BUTTONS AT BOTTOM
            btnCopyAll = new Button
            {
                Text = "📋 Copy Account Details",
                Font = new Font("Segoe UI", 9F, FontStyle.Bold),
                ForeColor = Color.FromArgb(30, 41, 59),
                BackColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Size = new Size(200, 38),
                Location = new Point(24, 515),
                Cursor = Cursors.Hand
            };
            btnCopyAll.FlatAppearance.BorderColor = Color.FromArgb(203, 213, 225);
            btnCopyAll.Click += (evSender, evArgs) =>
            {
                var summary = $"SmartBank Account Details\nName: {SessionManager.Instance.FullName}\nAccount #: {SessionManager.Instance.AccountNumber}\nUsername: {SessionManager.Instance.Username}\nEmail: {SessionManager.Instance.Email}\nPhone: {SessionManager.Instance.PhoneNumber}";
                Clipboard.SetText(summary);
                MessageBox.Show("All account details copied to clipboard!", "Copied", MessageBoxButtons.OK, MessageBoxIcon.Information);
            };

            btnChangePass = new Button
            {
                Text = "🔒 Security Settings",
                Font = new Font("Segoe UI", 9F, FontStyle.Bold),
                ForeColor = Color.FromArgb(37, 99, 235),
                BackColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Size = new Size(210, 38),
                Location = new Point(236, 515),
                Cursor = Cursors.Hand
            };
            btnChangePass.FlatAppearance.BorderColor = Color.FromArgb(191, 219, 254);
            btnChangePass.Click += (evSender, evArgs) =>
            {
                RequestedPasswordChange = true;
                this.Close();
            };

            btnClose = new Button
            {
                Text = "Close",
                Font = new Font("Segoe UI", 9.5F, FontStyle.Bold),
                ForeColor = Color.White,
                BackColor = Color.FromArgb(15, 23, 42),
                FlatStyle = FlatStyle.Flat,
                Size = new Size(422, 38),
                Location = new Point(24, 562),
                Cursor = Cursors.Hand
            };
            btnClose.FlatAppearance.BorderSize = 0;
            btnClose.Click += (evSender, evArgs) => this.Close();

            this.Controls.AddRange(new Control[] { btnClose, btnChangePass, btnCopyAll, pnlBankingInfo, pnlPersonalInfo, pnlTop });
        }

        private void SetEditMode(bool isEdit)
        {
            _isEditMode = isEdit;
            pnlViewFields.Visible = !isEdit;
            pnlEditFields.Visible = isEdit;
            btnEditProfile.Visible = !isEdit;
            lblEditError.Visible = false;

            if (isEdit)
            {
                var s = SessionManager.Instance;
                txtEditFullName.Text = s.FullName;
                txtEditEmail.Text = s.Email;
                txtEditPhone.Text = s.PhoneNumber ?? "";
                txtEditFullName.Focus();
            }
        }

        private async void BtnSaveProfile_Click(object? sender, EventArgs e)
        {
            lblEditError.Visible = false;
            var fullName = txtEditFullName.Text.Trim();
            var email = txtEditEmail.Text.Trim();
            var phone = txtEditPhone.Text.Trim();

            if (string.IsNullOrEmpty(fullName))
            {
                lblEditError.Text = "⚠️ Full Name is required.";
                lblEditError.Visible = true;
                txtEditFullName.Focus();
                return;
            }

            if (string.IsNullOrEmpty(email) || !email.Contains('@') || !email.Contains('.'))
            {
                lblEditError.Text = "⚠️ Please enter a valid Email Address.";
                lblEditError.Visible = true;
                txtEditEmail.Focus();
                return;
            }

            btnSaveProfile.Enabled = false;
            btnSaveProfile.Text = "Saving...";

            try
            {
                var payload = new
                {
                    fullName,
                    email,
                    phoneNumber = string.IsNullOrWhiteSpace(phone) ? null : phone
                };

                var res = await ApiClient.PutAsync<ApiResponse<LoginResponse>>("auth/profile", payload);
                if (res != null && res.Success && res.Data != null)
                {
                    // Update session
                    SessionManager.Instance.UpdateProfile(res.Data.FullName, res.Data.Email, res.Data.PhoneNumber);
                    HasChangesSaved = true;

                    // Update UI
                    lblName.Text = res.Data.FullName;
                    lblAvatar.Text = GetInitials(res.Data.FullName, res.Data.Username);
                    lblEmailVal.Text = res.Data.Email;
                    lblPhoneVal.Text = string.IsNullOrEmpty(res.Data.PhoneNumber) ? "+880 1711-000000" : res.Data.PhoneNumber;

                    SetEditMode(false);
                    MessageBox.Show("Your profile details have been updated successfully.", "Profile Updated", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                else
                {
                    lblEditError.Text = $"⚠️ {res?.Message ?? "Failed to update profile."}";
                    lblEditError.Visible = true;
                }
            }
            catch (ApiException ex)
            {
                lblEditError.Text = $"⚠️ {ex.Message}";
                lblEditError.Visible = true;
            }
            catch (Exception ex)
            {
                lblEditError.Text = $"⚠️ Error: {ex.Message}";
                lblEditError.Visible = true;
            }
            finally
            {
                btnSaveProfile.Enabled = true;
                btnSaveProfile.Text = "💾 Save Changes";
            }
        }

        private static string GetInitials(string fullName, string username)
        {
            if (!string.IsNullOrEmpty(fullName))
            {
                var parts = fullName.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                return parts.Length >= 2 ? $"{parts[0][0]}{parts[1][0]}".ToUpper() : fullName.Substring(0, Math.Min(2, fullName.Length)).ToUpper();
            }
            return !string.IsNullOrEmpty(username) ? username.Substring(0, Math.Min(2, username.Length)).ToUpper() : "SB";
        }

        private static void AddViewRow(Panel parent, string label, string value, int top, out Label valLabel)
        {
            var lblKey = new Label
            {
                Text = label,
                Font = new Font("Segoe UI", 8.5F),
                ForeColor = Color.FromArgb(100, 116, 139),
                Location = new Point(16, top),
                AutoSize = true
            };

            valLabel = new Label
            {
                Text = value,
                Font = new Font("Segoe UI", 9F, FontStyle.Bold),
                ForeColor = Color.FromArgb(15, 23, 42),
                Location = new Point(155, top),
                AutoSize = true
            };

            parent.Controls.AddRange(new Control[] { lblKey, valLabel });
        }
    }
}
