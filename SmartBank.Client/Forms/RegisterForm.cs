using System;
using System.Drawing;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using SmartBank.Client.Exceptions;
using SmartBank.Client.Models.Auth;
using SmartBank.Client.Security;
using SmartBank.Client.Services;

namespace SmartBank.Client.Forms
{
    public partial class RegisterForm : Form
    {
        private readonly AuthService _authService = new();

        public RegisterForm()
        {
            InitializeComponent();
        }

        private void txtPassword_TextChanged(object sender, EventArgs e)
        {
            UpdatePasswordStrength(txtPassword.Text);
        }

        private void UpdatePasswordStrength(string password)
        {
            if (string.IsNullOrEmpty(password))
            {
                progressBarPassword.Value = 0;
                lblStrength.Text = string.Empty;
                lblPasswordRequirements.Text = "Password must have 8+ chars, uppercase, lowercase, number, special char";
                lblPasswordRequirements.ForeColor = Color.DimGray;
                return;
            }

            int score = 0;
            if (password.Length >= 8) score++;
            if (Regex.IsMatch(password, @"[A-Z]")) score++;
            if (Regex.IsMatch(password, @"[a-z]")) score++;
            if (Regex.IsMatch(password, @"\d")) score++;
            if (Regex.IsMatch(password, @"[^\da-zA-Z]")) score++;

            progressBarPassword.Value = score;

            switch (score)
            {
                case 0:
                case 1:
                    lblStrength.Text = "Weak";
                    lblStrength.ForeColor = Color.Red;
                    break;
                case 2:
                case 3:
                    lblStrength.Text = "Medium";
                    lblStrength.ForeColor = Color.DarkOrange;
                    break;
                case 4:
                    lblStrength.Text = "Strong";
                    lblStrength.ForeColor = Color.RoyalBlue;
                    break;
                case 5:
                    lblStrength.Text = "Very Strong";
                    lblStrength.ForeColor = Color.Green;
                    break;
            }

            if (score == 5)
            {
                lblPasswordRequirements.Text = "✓ Password meets all requirements";
                lblPasswordRequirements.ForeColor = Color.Green;
            }
            else
            {
                lblPasswordRequirements.Text = "Password must have 8+ chars, uppercase, lowercase, number, special char";
                lblPasswordRequirements.ForeColor = Color.DimGray;
            }
        }

        private void chkShowPassword_CheckedChanged(object sender, EventArgs e)
        {
            var pChar = chkShowPassword.Checked ? '\0' : '●';
            txtPassword.PasswordChar = pChar;
            txtConfirmPassword.PasswordChar = pChar;
        }

        private async void btnRegister_Click(object sender, EventArgs e)
        {
            var fullName = txtFullName.Text.Trim();
            var email = txtEmail.Text.Trim();
            var phone = txtPhone.Text.Trim();
            var username = txtUsername.Text.Trim();
            var password = txtPassword.Text;
            var confirmPassword = txtConfirmPassword.Text;

            // Client-side validations
            if (string.IsNullOrWhiteSpace(fullName) || fullName.Length < 2)
            {
                ShowError("Full name must be at least 2 characters.");
                txtFullName.Focus();
                return;
            }

            if (string.IsNullOrWhiteSpace(email) || !email.Contains("@") || !email.Contains("."))
            {
                ShowError("Please enter a valid email address.");
                txtEmail.Focus();
                return;
            }

            if (string.IsNullOrWhiteSpace(username) || username.Length < 3)
            {
                ShowError("Username must be at least 3 characters.");
                txtUsername.Focus();
                return;
            }

            if (!Regex.IsMatch(username, @"^[a-zA-Z0-9_]+$"))
            {
                ShowError("Username can only contain letters, numbers, and underscores.");
                txtUsername.Focus();
                return;
            }

            if (password.Length < 8)
            {
                ShowError("Password must be at least 8 characters long.");
                txtPassword.Focus();
                return;
            }

            if (password != confirmPassword)
            {
                ShowError("Password and confirm password do not match.");
                txtConfirmPassword.Focus();
                return;
            }

            SetLoading(true);
            HideError();

            try
            {
                var request = new RegisterRequest
                {
                    FullName = fullName,
                    Email = email,
                    PhoneNumber = string.IsNullOrWhiteSpace(phone) ? null : phone,
                    Username = username,
                    Password = password,
                    ConfirmPassword = confirmPassword
                };

                var response = await _authService.RegisterAsync(request);
                if (response.Success && response.Data != null)
                {
                    MessageBox.Show(
                        $"Welcome to SmartBank, {response.Data.FullName}!\nYour account number is: {response.Data.AccountNumber}\nInitial Balance: ৳{response.Data.Balance:N2}",
                        "Registration Successful",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Information);

                    this.Close();
                }
                else
                {
                    ShowError(response.Message ?? "Registration failed.");
                }
            }
            catch (ApiException ex)
            {
                var msg = ex.Errors.Count > 0 ? string.Join("\n", ex.Errors) : ex.Message;
                ShowError(msg);
            }
            catch (Exception ex)
            {
                ShowError($"Connection error: {ex.Message}");
            }
            finally
            {
                SetLoading(false);
            }
        }

        private void btnBackToLogin_Click(object sender, EventArgs e)
        {
            this.Close();
        }

        private void SetLoading(bool isLoading)
        {
            pnlLoading.Visible = isLoading;
            btnRegister.Enabled = !isLoading;
            btnBackToLogin.Enabled = !isLoading;
            txtFullName.Enabled = !isLoading;
            txtEmail.Enabled = !isLoading;
            txtPhone.Enabled = !isLoading;
            txtUsername.Enabled = !isLoading;
            txtPassword.Enabled = !isLoading;
            txtConfirmPassword.Enabled = !isLoading;
        }

        private void ShowError(string message)
        {
            lblError.Text = message;
            lblError.ForeColor = Color.Red;
            lblError.Visible = true;
        }

        private void HideError()
        {
            lblError.Visible = false;
        }
    }
}
