using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using SmartBank.Client.Exceptions;
using SmartBank.Client.Models.Common;
using SmartBank.Client.Services;
using static SmartBank.Client.Forms.AdminDashboardForm;

namespace SmartBank.Client.Forms
{
    public class AdminEditUserDialog : Form
    {
        private readonly AdminUserItem _user;
        private TextBox txtFullName = null!;
        private TextBox txtUsername = null!;
        private TextBox txtEmail = null!;
        private TextBox txtPhoneNumber = null!;
        private TextBox txtAccountNumber = null!;
        private TextBox txtBalance = null!;
        private ComboBox cmbRole = null!;
        private ComboBox cmbAccountStatus = null!;
        private ComboBox cmbUserStatus = null!;
        private Button btnSave = null!;
        private Button btnCancel = null!;
        private Label lblError = null!;

        public AdminEditUserDialog(AdminUserItem user)
        {
            _user = user;
            InitializeComponents();
        }

        private void InitializeComponents()
        {
            this.Text = $"Edit User: {_user.FullName} (@{_user.Username}) — SmartBank Admin";
            this.ClientSize = new Size(540, 680);
            this.StartPosition = FormStartPosition.CenterParent;
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.BackColor = Color.FromArgb(243, 246, 251);
            this.Font = new Font("Segoe UI", 9F, FontStyle.Regular, GraphicsUnit.Point);

            // 1. TOP HEADER BANNER
            var pnlHeader = new Panel
            {
                Dock = DockStyle.Top,
                Height = 90,
                BackColor = Color.FromArgb(15, 23, 42) // Slate 900
            };

            var lblTitle = new Label
            {
                Text = "✏️ Edit User & Account Details",
                Font = new Font("Segoe UI", 13.5F, FontStyle.Bold),
                ForeColor = Color.White,
                Location = new Point(20, 16),
                AutoSize = true
            };

            var lblSubtitle = new Label
            {
                Text = $"User ID: #{_user.Id} • Primary Account: {_user.AccountNumber ?? "N/A"}",
                Font = new Font("Segoe UI", 8.5F),
                ForeColor = Color.FromArgb(148, 163, 184),
                Location = new Point(22, 48),
                AutoSize = true
            };

            pnlHeader.Controls.AddRange(new Control[] { lblTitle, lblSubtitle });

            // 2. MAIN SCROLLABLE/ORGANIZED EDIT CARD
            var pnlCard = new Panel
            {
                Location = new Point(20, 105),
                Size = new Size(500, 505),
                BackColor = Color.White,
                AutoScroll = false
            };
            pnlCard.Paint += (s, e) =>
            {
                using var pen = new Pen(Color.FromArgb(226, 232, 240), 1);
                e.Graphics.DrawRectangle(pen, 0, 0, pnlCard.Width - 1, pnlCard.Height - 1);
            };

            int currentY = 16;

            // Section: Personal Information
            var lblSec1 = CreateSectionHeader("PERSONAL & LOGIN CREDENTIALS", currentY);
            pnlCard.Controls.Add(lblSec1);
            currentY += 26;

            // Full Name
            pnlCard.Controls.Add(CreateFieldLabel("Full Name *", 20, currentY));
            txtFullName = CreateStyledTextBox(_user.FullName, 20, currentY + 18, 220);
            pnlCard.Controls.Add(txtFullName);

            // Username
            pnlCard.Controls.Add(CreateFieldLabel("Username *", 260, currentY));
            txtUsername = CreateStyledTextBox(_user.Username, 260, currentY + 18, 220);
            pnlCard.Controls.Add(txtUsername);
            currentY += 60;

            // Email Address
            pnlCard.Controls.Add(CreateFieldLabel("Email Address *", 20, currentY));
            txtEmail = CreateStyledTextBox(_user.Email, 20, currentY + 18, 220);
            pnlCard.Controls.Add(txtEmail);

            // Phone Number
            pnlCard.Controls.Add(CreateFieldLabel("Phone Number", 260, currentY));
            txtPhoneNumber = CreateStyledTextBox(_user.PhoneNumber ?? "", 260, currentY + 18, 220);
            pnlCard.Controls.Add(txtPhoneNumber);
            currentY += 66;

            // Section: Banking Account Details
            var lblSec2 = CreateSectionHeader("BANKING & BALANCE SETTINGS", currentY);
            pnlCard.Controls.Add(lblSec2);
            currentY += 26;

            // Account Number
            pnlCard.Controls.Add(CreateFieldLabel("Account Number", 20, currentY));
            txtAccountNumber = CreateStyledTextBox(_user.AccountNumber ?? "", 20, currentY + 18, 220);
            pnlCard.Controls.Add(txtAccountNumber);

            // Balance
            pnlCard.Controls.Add(CreateFieldLabel("Current Balance (৳)", 260, currentY));
            txtBalance = CreateStyledTextBox(_user.Balance.ToString("F2", CultureInfo.InvariantCulture), 260, currentY + 18, 220);
            pnlCard.Controls.Add(txtBalance);
            currentY += 60;

            // Role
            pnlCard.Controls.Add(CreateFieldLabel("System Role *", 20, currentY));
            cmbRole = new ComboBox
            {
                Location = new Point(20, currentY + 18),
                Size = new Size(220, 28),
                DropDownStyle = ComboBoxStyle.DropDownList,
                Font = new Font("Segoe UI", 9.5F)
            };
            cmbRole.Items.AddRange(new object[] { "Customer", "Admin" });
            cmbRole.SelectedItem = _user.Role.Equals("Admin", StringComparison.OrdinalIgnoreCase) ? "Admin" : "Customer";
            pnlCard.Controls.Add(cmbRole);

            // Account Status (Active/Frozen)
            pnlCard.Controls.Add(CreateFieldLabel("Account Status (Freeze Flag)", 260, currentY));
            cmbAccountStatus = new ComboBox
            {
                Location = new Point(260, currentY + 18),
                Size = new Size(220, 28),
                DropDownStyle = ComboBoxStyle.DropDownList,
                Font = new Font("Segoe UI", 9.5F)
            };
            cmbAccountStatus.Items.AddRange(new object[] { "🟢 Active (Unfrozen)", "❄️ Frozen (Transactions Disabled)" });
            cmbAccountStatus.SelectedIndex = _user.AccountActive ? 0 : 1;
            pnlCard.Controls.Add(cmbAccountStatus);
            currentY += 60;

            // User Security Status (Active / Suspended)
            pnlCard.Controls.Add(CreateFieldLabel("User Risk / Suspension Status", 20, currentY));
            cmbUserStatus = new ComboBox
            {
                Location = new Point(20, currentY + 18),
                Size = new Size(460, 28),
                DropDownStyle = ComboBoxStyle.DropDownList,
                Font = new Font("Segoe UI", 9.5F)
            };
            cmbUserStatus.Items.AddRange(new object[] { "🟢 Active (Normal Login & Access)", "🚫 Suspended (Login & Banking Restricted)" });
            cmbUserStatus.SelectedIndex = _user.IsSuspended ? 1 : 0;
            pnlCard.Controls.Add(cmbUserStatus);
            currentY += 56;

            // Error label
            lblError = new Label
            {
                Location = new Point(20, currentY),
                Size = new Size(460, 36),
                Font = new Font("Segoe UI", 8.5F, FontStyle.Bold),
                ForeColor = Color.FromArgb(225, 29, 72),
                TextAlign = ContentAlignment.MiddleLeft,
                Visible = false
            };
            pnlCard.Controls.Add(lblError);

            // 3. BOTTOM BUTTONS
            btnCancel = new Button
            {
                Text = "Cancel",
                Font = new Font("Segoe UI", 9.5F, FontStyle.Bold),
                ForeColor = Color.FromArgb(71, 85, 105),
                BackColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Size = new Size(150, 42),
                Location = new Point(20, 624),
                Cursor = Cursors.Hand
            };
            btnCancel.FlatAppearance.BorderColor = Color.FromArgb(203, 213, 225);
            btnCancel.Click += (s, e) => { this.DialogResult = DialogResult.Cancel; this.Close(); };

            btnSave = new Button
            {
                Text = "💾 Save Changes",
                Font = new Font("Segoe UI", 9.5F, FontStyle.Bold),
                ForeColor = Color.White,
                BackColor = Color.FromArgb(37, 99, 235), // Blue 600
                FlatStyle = FlatStyle.Flat,
                Size = new Size(335, 42),
                Location = new Point(185, 624),
                Cursor = Cursors.Hand
            };
            btnSave.FlatAppearance.BorderSize = 0;
            btnSave.Click += BtnSave_Click;

            this.Controls.AddRange(new Control[] { btnSave, btnCancel, pnlCard, pnlHeader });
        }

        private static Label CreateSectionHeader(string text, int top)
        {
            return new Label
            {
                Text = text,
                Font = new Font("Segoe UI", 8F, FontStyle.Bold),
                ForeColor = Color.FromArgb(100, 116, 139),
                Location = new Point(20, top),
                AutoSize = true
            };
        }

        private static Label CreateFieldLabel(string text, int left, int top)
        {
            return new Label
            {
                Text = text,
                Font = new Font("Segoe UI", 8.5F, FontStyle.Bold),
                ForeColor = Color.FromArgb(51, 65, 85),
                Location = new Point(left, top),
                AutoSize = true
            };
        }

        private static TextBox CreateStyledTextBox(string text, int left, int top, int width)
        {
            return new TextBox
            {
                Text = text,
                Location = new Point(left, top),
                Size = new Size(width, 28),
                Font = new Font("Segoe UI", 9.5F),
                BorderStyle = BorderStyle.FixedSingle
            };
        }

        private async void BtnSave_Click(object? sender, EventArgs e)
        {
            lblError.Visible = false;
            var fullName = txtFullName.Text.Trim();
            var username = txtUsername.Text.Trim();
            var email = txtEmail.Text.Trim();
            var phone = txtPhoneNumber.Text.Trim();
            var accountNumber = txtAccountNumber.Text.Trim();
            var balanceText = txtBalance.Text.Trim();

            if (string.IsNullOrEmpty(fullName))
            {
                ShowError("Full Name is required.");
                txtFullName.Focus();
                return;
            }

            if (string.IsNullOrEmpty(username))
            {
                ShowError("Username is required.");
                txtUsername.Focus();
                return;
            }

            if (string.IsNullOrEmpty(email) || !email.Contains('@') || !email.Contains('.'))
            {
                ShowError("A valid Email Address is required.");
                txtEmail.Focus();
                return;
            }

            decimal balance = _user.Balance;
            if (!string.IsNullOrEmpty(balanceText))
            {
                if (!decimal.TryParse(balanceText, NumberStyles.Any, CultureInfo.InvariantCulture, out balance) || balance < 0)
                {
                    ShowError("Please enter a valid non-negative balance amount.");
                    txtBalance.Focus();
                    return;
                }
            }

            btnSave.Enabled = false;
            btnSave.Text = "Saving Changes...";

            try
            {
                var role = cmbRole.SelectedItem?.ToString() ?? "Customer";
                var accountActive = cmbAccountStatus.SelectedIndex == 0;
                var userStatus = cmbUserStatus.SelectedIndex == 1 ? "Suspended" : "Active";

                var payload = new
                {
                    userId = _user.Id,
                    fullName,
                    username,
                    email,
                    phoneNumber = string.IsNullOrWhiteSpace(phone) ? null : phone,
                    role,
                    status = userStatus,
                    accountNumber = string.IsNullOrWhiteSpace(accountNumber) ? null : accountNumber,
                    balance,
                    accountActive
                };

                var res = await ApiClient.PostAsync<ApiResponse<bool>>("admin/update-user", payload);
                if (res != null && res.Success)
                {
                    MessageBox.Show(res.Message, "User Updated", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    this.DialogResult = DialogResult.OK;
                    this.Close();
                }
                else
                {
                    ShowError(res?.Message ?? "Failed to update user.");
                }
            }
            catch (ApiException ex)
            {
                ShowError(ex.Message);
            }
            catch (Exception ex)
            {
                ShowError($"Unexpected error: {ex.Message}");
            }
            finally
            {
                btnSave.Enabled = true;
                btnSave.Text = "💾 Save Changes";
            }
        }

        private void ShowError(string message)
        {
            lblError.Text = $"⚠️ {message}";
            lblError.Visible = true;
        }
    }
}
