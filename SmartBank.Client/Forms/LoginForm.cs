using System;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using SmartBank.Client.Exceptions;
using SmartBank.Client.Models.Auth;
using SmartBank.Client.Security;
using SmartBank.Client.Services;

namespace SmartBank.Client.Forms
{
    public partial class LoginForm : Form
    {
        private readonly AuthService _authService = new();
        private int _lockoutSecondsRemaining = 0;

        public LoginForm()
        {
            InitializeComponent();
        }

        private void LoginForm_Load(object sender, EventArgs e)
        {
            txtUsername.Focus();
            txtUsername.KeyDown += TxtUsername_KeyDown;
            txtPassword.KeyDown += TxtPassword_KeyDown;
        }

        private void TxtUsername_KeyDown(object? sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)
            {
                e.SuppressKeyPress = true;
                txtPassword.Focus();
            }
        }

        private void TxtPassword_KeyDown(object? sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)
            {
                e.SuppressKeyPress = true;
                btnLogin.PerformClick();
            }
        }

        private void chkShowPassword_CheckedChanged(object sender, EventArgs e)
        {
            txtPassword.PasswordChar = chkShowPassword.Checked ? '\0' : '●';
        }

        private async void btnLogin_Click(object sender, EventArgs e)
        {
            var username = txtUsername.Text.Trim();
            var password = txtPassword.Text;

            if (string.IsNullOrWhiteSpace(username))
            {
                ShowError("Please enter your username.");
                txtUsername.Focus();
                return;
            }

            if (string.IsNullOrEmpty(password))
            {
                ShowError("Please enter your password.");
                txtPassword.Focus();
                return;
            }

            SetLoading(true);
            HideError();

            try
            {
                var request = new LoginRequest
                {
                    Username = username,
                    Password = password
                };

                var response = await _authService.LoginAsync(request);
                if (response.Success && response.Data != null)
                {
                    NavigateToDashboard(response.Data.Role);
                }
                else
                {
                    ShowError(response.Message ?? "Invalid username or password");
                }
            }
            catch (ApiException ex)
            {
                if (ex.StatusCode == 429)
                {
                    var seconds = ex.RetryAfterSeconds ?? (15 * 60);
                    StartCountdown(seconds, "Too many login attempts. Please wait before trying again.");
                }
                else if (ex.StatusCode == 403)
                {
                    var msg = ex.Errors.FirstOrDefault() ?? ex.Message;
                    ShowError(msg);
                }
                else if (ex.StatusCode == 401)
                {
                    ShowError("Invalid username or password");
                }
                else
                {
                    var msg = ex.Errors.Count > 0 ? string.Join("\n", ex.Errors) : ex.Message;
                    ShowError(msg);
                }
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

        private void NavigateToDashboard(string role)
        {
            this.Close();
        }

        private void btnRegister_Click(object sender, EventArgs e)
        {
            this.Hide();
            var registerForm = new RegisterForm();
            registerForm.FormClosed += (s, args) =>
            {
                if (SessionManager.Instance.IsAuthenticated)
                {
                    this.Close();
                }
                else
                {
                    this.Show();
                    txtUsername.Focus();
                }
            };
            registerForm.Show();
        }

        private void SetLoading(bool isLoading)
        {
            pnlLoading.Visible = isLoading;
            btnLogin.Enabled = !isLoading && _lockoutSecondsRemaining <= 0;
            btnRegister.Enabled = !isLoading;
            txtUsername.Enabled = !isLoading && _lockoutSecondsRemaining <= 0;
            txtPassword.Enabled = !isLoading && _lockoutSecondsRemaining <= 0;
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

        private void StartCountdown(int totalSeconds, string initialMessage)
        {
            _lockoutSecondsRemaining = totalSeconds;
            ShowError(initialMessage);
            lblCountdown.Text = $"Retry available in: {TimeSpan.FromSeconds(_lockoutSecondsRemaining):mm\\:ss}";
            lblCountdown.Visible = true;

            txtUsername.Enabled = false;
            txtPassword.Enabled = false;
            btnLogin.Enabled = false;

            timerCountdown.Start();
        }

        private void timerCountdown_Tick(object sender, EventArgs e)
        {
            _lockoutSecondsRemaining--;
            if (_lockoutSecondsRemaining <= 0)
            {
                timerCountdown.Stop();
                lblCountdown.Visible = false;
                HideError();

                txtUsername.Enabled = true;
                txtPassword.Enabled = true;
                btnLogin.Enabled = true;
                txtUsername.Focus();
            }
            else
            {
                lblCountdown.Text = $"Retry available in: {TimeSpan.FromSeconds(_lockoutSecondsRemaining):mm\\:ss}";
            }
        }
    }
}
