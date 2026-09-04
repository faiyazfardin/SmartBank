using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using SmartBank.Client.Exceptions;
using SmartBank.Client.Models.Auth;
using SmartBank.Client.Models.Common;
using SmartBank.Client.Models.Transactions;
using SmartBank.Client.Security;
using SmartBank.Client.Services;

namespace SmartBank.Client.Forms
{
    public class DashboardForm : Form
    {
        private Panel pnlUserBadge = null!;
        private Label lblUserAvatar = null!;
        private Label lblUserName = null!;
        private Label lblUserTier = null!;
        private Label lblBalance = null!;
        private Label lblAccountNumber = null!;
        private Label lblInflow = null!;
        private Label lblOutflow = null!;
        private Label lblLastSync = null!;
        private Button btnCopyAccount = null!;
        private Button btnDeposit = null!;
        private Button btnTransfer = null!;
        private Button btnWithdraw = null!;
        private Button btnPayBill = null!;
        private Button btnExportStatement = null!;
        private Button btnRefresh = null!;
        private Button btnLogout = null!;
        private Button btnToggleBalance = null!;
        private ComboBox cmbFilter = null!;
        private DataGridView dgvTransactions = null!;
        private Panel pnlDebitCard = null!;
        private List<TransactionItem> _allTransactions = new();
        private System.Windows.Forms.Timer _liveSyncTimer = null!;
        private bool _isBalanceHidden = false;

        public DashboardForm()
        {
            InitializeComponents();
            LoadUserData();
            SetupLiveTimer();
            _ = RefreshBalanceAndHistoryAsync();
        }

        private void SetupLiveTimer()
        {
            _liveSyncTimer = new System.Windows.Forms.Timer { Interval = 4000 }; // Auto-sync every 4 seconds
            _liveSyncTimer.Tick += async (s, e) =>
            {
                if (!this.IsDisposed && this.Visible)
                {
                    await RefreshBalanceAndHistoryAsync(isBackgroundPoll: true);
                }
            };
            _liveSyncTimer.Start();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            _liveSyncTimer?.Stop();
            _liveSyncTimer?.Dispose();
            base.OnFormClosing(e);
        }

        private void InitializeComponents()
        {
            this.Text = "SmartBank — Premier Digital Banking";
            this.ClientSize = new Size(1080, 750);
            this.StartPosition = FormStartPosition.CenterScreen;
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.BackColor = Color.FromArgb(243, 246, 251);
            this.Font = new Font("Segoe UI", 9F, FontStyle.Regular, GraphicsUnit.Point);

            // 1. TOP HEADER BAR
            var pnlHeader = new Panel
            {
                Dock = DockStyle.Top,
                Height = 84,
                BackColor = Color.FromArgb(15, 23, 42) // Slate 900
            };

            var lblLogo = new Label
            {
                Text = "SmartBank",
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 18F, FontStyle.Bold),
                Location = new Point(24, 14),
                AutoSize = true
            };

            var lblSubtitle = new Label
            {
                Text = "Online Banking & Wealth Management",
                ForeColor = Color.FromArgb(148, 163, 184),
                Font = new Font("Segoe UI", 8.5F),
                Location = new Point(26, 50),
                AutoSize = true
            };

            // bKash-style Clickable User Profile Badge Card
            pnlUserBadge = new Panel
            {
                Location = new Point(580, 14),
                Size = new Size(330, 56),
                BackColor = Color.FromArgb(30, 41, 59),
                Cursor = Cursors.Hand
            };
            pnlUserBadge.Paint += (s, e) => DrawBorder(e.Graphics, pnlUserBadge.ClientRectangle, Color.FromArgb(51, 65, 85));
            pnlUserBadge.MouseEnter += (s, e) => pnlUserBadge.BackColor = Color.FromArgb(45, 58, 82);
            pnlUserBadge.MouseLeave += (s, e) => pnlUserBadge.BackColor = Color.FromArgb(30, 41, 59);
            pnlUserBadge.Click += (s, e) => OpenUserProfileDialog();

            lblUserAvatar = new Label
            {
                Text = "SB",
                Font = new Font("Segoe UI", 11F, FontStyle.Bold),
                ForeColor = Color.White,
                BackColor = Color.FromArgb(37, 99, 235),
                Size = new Size(40, 40),
                Location = new Point(8, 8),
                TextAlign = ContentAlignment.MiddleCenter,
                Cursor = Cursors.Hand
            };
            lblUserAvatar.Click += (s, e) => OpenUserProfileDialog();

            lblUserName = new Label
            {
                Text = "Customer Name",
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 10.5F, FontStyle.Bold),
                Location = new Point(56, 8),
                AutoSize = true,
                Cursor = Cursors.Hand
            };
            lblUserName.Click += (s, e) => OpenUserProfileDialog();

            lblUserTier = new Label
            {
                Text = "● Verified Account • Tap for Details ➔",
                ForeColor = Color.FromArgb(52, 211, 153),
                Font = new Font("Segoe UI", 8F, FontStyle.Bold),
                Location = new Point(56, 30),
                AutoSize = true,
                Cursor = Cursors.Hand
            };
            lblUserTier.Click += (s, e) => OpenUserProfileDialog();

            pnlUserBadge.Controls.AddRange(new Control[] { lblUserAvatar, lblUserName, lblUserTier });

            btnLogout = new Button
            {
                Text = "Logout",
                Font = new Font("Segoe UI", 9.5F, FontStyle.Bold),
                ForeColor = Color.White,
                BackColor = Color.FromArgb(225, 29, 72), // Rose 600
                FlatStyle = FlatStyle.Flat,
                Size = new Size(110, 42),
                Location = new Point(935, 20),
                Cursor = Cursors.Hand
            };
            btnLogout.FlatAppearance.BorderSize = 0;
            btnLogout.Click += BtnLogout_Click;

            pnlHeader.Controls.AddRange(new Control[] { lblLogo, lblSubtitle, pnlUserBadge, btnLogout });

            // 2A. VIRTUAL DEBIT CARD (LEFT PANEL)
            pnlDebitCard = new Panel
            {
                Location = new Point(24, 98),
                Size = new Size(350, 215),
                BackColor = Color.FromArgb(30, 41, 59)
            };
            pnlDebitCard.Paint += PnlDebitCard_Paint;

            // 2B. FINANCIAL OVERVIEW & BALANCE (CENTER-RIGHT PANEL)
            var pnlBalanceCard = new Panel
            {
                Location = new Point(390, 98),
                Size = new Size(665, 122),
                BackColor = Color.White
            };
            pnlBalanceCard.Paint += (s, e) => DrawBorder(e.Graphics, pnlBalanceCard.ClientRectangle, Color.FromArgb(226, 232, 240));

            var lblBalHeader = new Label
            {
                Text = "AVAILABLE CHECKING BALANCE",
                Font = new Font("Segoe UI", 8.5F, FontStyle.Bold),
                ForeColor = Color.FromArgb(100, 116, 139),
                Location = new Point(20, 14),
                AutoSize = true
            };

            lblBalance = new Label
            {
                Text = "৳0.00",
                Font = new Font("Segoe UI", 26F, FontStyle.Bold),
                ForeColor = Color.FromArgb(15, 23, 42),
                Location = new Point(18, 34),
                AutoSize = true,
                Cursor = Cursors.Hand
            };
            lblBalance.Click += (s, e) => ToggleBalanceVisibility();

            btnToggleBalance = new Button
            {
                Text = "👁️ Hide",
                Font = new Font("Segoe UI", 7.5F, FontStyle.Bold),
                ForeColor = Color.FromArgb(100, 116, 139),
                BackColor = Color.FromArgb(241, 245, 249),
                FlatStyle = FlatStyle.Flat,
                Size = new Size(65, 24),
                Location = new Point(220, 12),
                Cursor = Cursors.Hand
            };
            btnToggleBalance.FlatAppearance.BorderSize = 0;
            btnToggleBalance.Click += (s, e) => ToggleBalanceVisibility();

            var lblAccHeader = new Label
            {
                Text = "ACCOUNT NUMBER",
                Font = new Font("Segoe UI", 8.5F, FontStyle.Bold),
                ForeColor = Color.FromArgb(100, 116, 139),
                Location = new Point(380, 14),
                AutoSize = true
            };

            lblAccountNumber = new Label
            {
                Text = "•••• •••• ••••",
                Font = new Font("Segoe UI", 12F, FontStyle.Bold),
                ForeColor = Color.FromArgb(37, 99, 235),
                Location = new Point(380, 36),
                AutoSize = true
            };

            btnCopyAccount = new Button
            {
                Text = "Copy",
                Font = new Font("Segoe UI", 8.5F, FontStyle.Bold),
                ForeColor = Color.FromArgb(71, 85, 105),
                BackColor = Color.FromArgb(241, 245, 249),
                FlatStyle = FlatStyle.Flat,
                Size = new Size(70, 30),
                Location = new Point(570, 34),
                Cursor = Cursors.Hand
            };
            btnCopyAccount.FlatAppearance.BorderSize = 0;
            btnCopyAccount.Click += BtnCopyAccount_Click;

            lblLastSync = new Label
            {
                Text = "Synced just now",
                Font = new Font("Segoe UI", 8F, FontStyle.Italic),
                ForeColor = Color.FromArgb(148, 163, 184),
                Location = new Point(20, 92),
                AutoSize = true
            };

            btnRefresh = new Button
            {
                Text = "Refresh",
                Font = new Font("Segoe UI", 8.5F, FontStyle.Bold),
                ForeColor = Color.FromArgb(37, 99, 235),
                BackColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Size = new Size(88, 28),
                Location = new Point(552, 82),
                Cursor = Cursors.Hand
            };
            btnRefresh.FlatAppearance.BorderColor = Color.FromArgb(226, 232, 240);
            btnRefresh.Click += async (s, e) => await RefreshBalanceAndHistoryAsync(isBackgroundPoll: false);

            pnlBalanceCard.Controls.AddRange(new Control[] { lblBalHeader, btnToggleBalance, lblBalance, lblAccHeader, lblAccountNumber, btnCopyAccount, lblLastSync, btnRefresh });

            // 2C. CASH FLOW STATS (INFLOW / OUTFLOW)
            var pnlInflow = new Panel
            {
                Location = new Point(390, 232),
                Size = new Size(325, 81),
                BackColor = Color.White
            };
            pnlInflow.Paint += (s, e) => DrawBorder(e.Graphics, pnlInflow.ClientRectangle, Color.FromArgb(226, 232, 240));

            var lblInflowTitle = new Label { Text = "▲ Total Inflow (Deposits / In)", Font = new Font("Segoe UI", 8.5F, FontStyle.Bold), ForeColor = Color.FromArgb(16, 185, 129), Location = new Point(16, 12), AutoSize = true };
            lblInflow = new Label { Text = "+৳0.00", Font = new Font("Segoe UI", 16F, FontStyle.Bold), ForeColor = Color.FromArgb(5, 150, 105), Location = new Point(16, 34), AutoSize = true };
            pnlInflow.Controls.AddRange(new Control[] { lblInflowTitle, lblInflow });

            var pnlOutflow = new Panel
            {
                Location = new Point(730, 232),
                Size = new Size(325, 81),
                BackColor = Color.White
            };
            pnlOutflow.Paint += (s, e) => DrawBorder(e.Graphics, pnlOutflow.ClientRectangle, Color.FromArgb(226, 232, 240));

            var lblOutflowTitle = new Label { Text = "▼ Total Outflow (Withdrawals / Bills)", Font = new Font("Segoe UI", 8.5F, FontStyle.Bold), ForeColor = Color.FromArgb(239, 68, 68), Location = new Point(16, 12), AutoSize = true };
            lblOutflow = new Label { Text = "-৳0.00", Font = new Font("Segoe UI", 16F, FontStyle.Bold), ForeColor = Color.FromArgb(220, 38, 38), Location = new Point(16, 34), AutoSize = true };
            pnlOutflow.Controls.AddRange(new Control[] { lblOutflowTitle, lblOutflow });

            // 3. ACTION BUTTONS TOOLBAR
            var pnlActions = new Panel
            {
                Location = new Point(24, 326),
                Size = new Size(1031, 58),
                BackColor = Color.Transparent
            };

            btnDeposit = CreateToolbarButton("Deposit Funds", Color.FromArgb(16, 185, 129), new Point(0, 4), BtnDeposit_Click);
            btnTransfer = CreateToolbarButton("Transfer Money", Color.FromArgb(37, 99, 235), new Point(208, 4), BtnTransfer_Click);
            btnWithdraw = CreateToolbarButton("ATM Withdrawal", Color.FromArgb(245, 158, 11), new Point(416, 4), BtnWithdraw_Click);
            btnPayBill = CreateToolbarButton("Pay Utility Bill", Color.FromArgb(139, 92, 246), new Point(624, 4), BtnPayBill_Click);
            btnExportStatement = CreateToolbarButton("Bank Statement", Color.FromArgb(71, 85, 105), new Point(832, 4), BtnExportStatement_Click);

            pnlActions.Controls.AddRange(new Control[] { btnDeposit, btnTransfer, btnWithdraw, btnPayBill, btnExportStatement });

            // 4. TRANSACTION HISTORY SECTION
            var pnlHistoryHeader = new Panel
            {
                Location = new Point(24, 390),
                Size = new Size(1031, 38),
                BackColor = Color.Transparent
            };

            var lblHistoryTitle = new Label
            {
                Text = "Live Transaction Activity & Statements",
                Font = new Font("Segoe UI", 11F, FontStyle.Bold),
                ForeColor = Color.FromArgb(15, 23, 42),
                Location = new Point(0, 6),
                AutoSize = true
            };

            var lblFilter = new Label
            {
                Text = "Filter:",
                Font = new Font("Segoe UI", 9F, FontStyle.Bold),
                ForeColor = Color.FromArgb(100, 116, 139),
                Location = new Point(810, 8),
                AutoSize = true
            };

            cmbFilter = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Font = new Font("Segoe UI", 9F),
                Size = new Size(165, 28),
                Location = new Point(860, 4)
            };
            cmbFilter.Items.AddRange(new object[] { "All Transactions", "Deposits", "Withdrawals", "Transfers Sent", "Transfers Received" });
            cmbFilter.SelectedIndex = 0;
            cmbFilter.SelectedIndexChanged += (s, e) => FilterTransactions();

            pnlHistoryHeader.Controls.AddRange(new Control[] { lblHistoryTitle, lblFilter, cmbFilter });

            // DataGridView for Transactions
            dgvTransactions = new DataGridView
            {
                Location = new Point(24, 432),
                Size = new Size(1031, 290),
                BackgroundColor = Color.White,
                BorderStyle = BorderStyle.None,
                RowHeadersVisible = false,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                ReadOnly = true,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                MultiSelect = false,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                RowTemplate = { Height = 40 }
            };
            dgvTransactions.EnableHeadersVisualStyles = false;
            dgvTransactions.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(241, 245, 249);
            dgvTransactions.ColumnHeadersDefaultCellStyle.ForeColor = Color.FromArgb(30, 41, 59);
            dgvTransactions.ColumnHeadersDefaultCellStyle.Font = new Font("Segoe UI", 9F, FontStyle.Bold);
            dgvTransactions.ColumnHeadersHeight = 36;
            dgvTransactions.DefaultCellStyle.Font = new Font("Segoe UI", 9F);
            dgvTransactions.DefaultCellStyle.SelectionBackColor = Color.FromArgb(239, 246, 255);
            dgvTransactions.DefaultCellStyle.SelectionForeColor = Color.FromArgb(15, 23, 42);
            dgvTransactions.AlternatingRowsDefaultCellStyle.BackColor = Color.FromArgb(248, 250, 252);

            dgvTransactions.Columns.Add(new DataGridViewTextBoxColumn { Name = "FormattedDate", HeaderText = "Date & Time", FillWeight = 25 });
            dgvTransactions.Columns.Add(new DataGridViewTextBoxColumn { Name = "TypeDescription", HeaderText = "Type", FillWeight = 30 });
            dgvTransactions.Columns.Add(new DataGridViewTextBoxColumn { Name = "FormattedAmount", HeaderText = "Amount", FillWeight = 25 });
            dgvTransactions.Columns.Add(new DataGridViewTextBoxColumn { Name = "Status", HeaderText = "Status", FillWeight = 20 });

            this.Controls.Add(dgvTransactions);
            this.Controls.Add(pnlHistoryHeader);
            this.Controls.Add(pnlActions);
            this.Controls.Add(pnlOutflow);
            this.Controls.Add(pnlInflow);
            this.Controls.Add(pnlBalanceCard);
            this.Controls.Add(pnlDebitCard);
            this.Controls.Add(pnlHeader);
        }

        private static void DrawBorder(Graphics g, Rectangle rect, Color color)
        {
            using var pen = new Pen(color, 1);
            g.DrawRectangle(pen, rect.X, rect.Y, rect.Width - 1, rect.Height - 1);
        }

        private void PnlDebitCard_Paint(object? sender, PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            var rect = pnlDebitCard.ClientRectangle;

            // Metallic gradient
            using var brush = new LinearGradientBrush(rect, Color.FromArgb(24, 34, 53), Color.FromArgb(15, 23, 42), 45F);
            g.FillRectangle(brush, rect);

            using var borderPen = new Pen(Color.FromArgb(71, 85, 105), 1);
            g.DrawRectangle(borderPen, 0, 0, rect.Width - 1, rect.Height - 1);

            // Gold Chip
            using var chipBrush = new SolidBrush(Color.FromArgb(234, 179, 8));
            g.FillRectangle(chipBrush, 24, 24, 40, 30);
            using var chipLinePen = new Pen(Color.FromArgb(161, 98, 7), 1);
            g.DrawRectangle(chipLinePen, 24, 24, 40, 30);
            g.DrawLine(chipLinePen, 24, 39, 64, 39);
            g.DrawLine(chipLinePen, 44, 24, 44, 54);

            // Contactless waves drawn using GDI+ arcs
            using var wavePen = new Pen(Color.FromArgb(203, 213, 225), 2);
            g.DrawArc(wavePen, 78, 30, 10, 18, -45, 90);
            g.DrawArc(wavePen, 82, 26, 14, 26, -45, 90);
            g.DrawArc(wavePen, 86, 22, 18, 34, -45, 90);

            // SmartBank Platinum
            using var fontBrand = new Font("Segoe UI", 11F, FontStyle.Bold);
            using var goldBrush = new SolidBrush(Color.FromArgb(250, 204, 21));
            g.DrawString("SmartBank PLATINUM", fontBrand, goldBrush, 135, 26);

            // Masked Card Number
            var acc = SessionManager.Instance.AccountNumber;
            var last4 = acc.Length >= 4 ? acc.Substring(acc.Length - 4) : "8970";
            using var fontCardNum = new Font("Consolas", 13.5F, FontStyle.Bold);
            g.DrawString($"4532  ••••  ••••  {last4}", fontCardNum, Brushes.White, 24, 90);

            // Cardholder & Expiry
            using var fontLabel = new Font("Segoe UI", 7F, FontStyle.Bold);
            using var fontVal = new Font("Segoe UI", 9F, FontStyle.Bold);
            using var textBrush = new SolidBrush(Color.FromArgb(148, 163, 184));

            g.DrawString("CARDHOLDER", fontLabel, textBrush, 24, 142);
            var name = string.IsNullOrEmpty(SessionManager.Instance.FullName) ? "VALUED CUSTOMER" : SessionManager.Instance.FullName.ToUpper();
            g.DrawString(name, fontVal, Brushes.White, 24, 158);

            g.DrawString("EXPIRES", fontLabel, textBrush, 200, 142);
            g.DrawString("09/30", fontVal, Brushes.White, 200, 158);

            g.DrawString("CVV", fontLabel, textBrush, 275, 142);
            g.DrawString("•••", fontVal, Brushes.White, 275, 158);
        }

        private Button CreateToolbarButton(string text, Color color, Point location, EventHandler onClick)
        {
            var btn = new Button
            {
                Text = text,
                Font = new Font("Segoe UI", 9.5F, FontStyle.Bold),
                ForeColor = Color.White,
                BackColor = color,
                FlatStyle = FlatStyle.Flat,
                Size = new Size(198, 48),
                Location = location,
                Cursor = Cursors.Hand
            };
            btn.FlatAppearance.BorderSize = 0;
            btn.Click += onClick;
            return btn;
        }

        private void ToggleBalanceVisibility()
        {
            _isBalanceHidden = !_isBalanceHidden;
            UpdateBalanceDisplay();
            btnToggleBalance.Text = _isBalanceHidden ? "👁️ Show" : "👁️ Hide";
        }

        private void UpdateBalanceDisplay()
        {
            if (_isBalanceHidden)
            {
                lblBalance.Text = "••••••••";
            }
            else
            {
                lblBalance.Text = $"৳{SessionManager.Instance.Balance:N2}";
            }
        }

        private void LoadUserData()
        {
            var s = SessionManager.Instance;
            lblUserName.Text = string.IsNullOrEmpty(s.FullName) ? s.Username : s.FullName;
            lblAccountNumber.Text = string.IsNullOrEmpty(s.AccountNumber) ? "N/A" : s.AccountNumber;
            UpdateBalanceDisplay();

            if (!string.IsNullOrEmpty(s.FullName))
            {
                var parts = s.FullName.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                lblUserAvatar.Text = parts.Length >= 2 ? $"{parts[0][0]}{parts[1][0]}".ToUpper() : s.FullName.Substring(0, Math.Min(2, s.FullName.Length)).ToUpper();
            }
            pnlDebitCard.Invalidate();
        }

        private void OpenUserProfileDialog()
        {
            using var dlg = new UserProfileDialog();
            dlg.ShowDialog(this);

            if (dlg.RequestedLogout)
            {
                BtnLogout_Click(this, EventArgs.Empty);
            }
            else if (dlg.RequestedPasswordChange)
            {
                BtnChangePassword_Click(this, EventArgs.Empty);
            }
        }

        private async Task RefreshBalanceAndHistoryAsync(bool isBackgroundPoll = false)
        {
            if (!isBackgroundPoll)
            {
                lblLastSync.Text = "Updating...";
            }

            try
            {
                var profileTask = ApiClient.GetAsync<ApiResponse<LoginResponse>>("auth/me");
                var historyTask = ApiClient.GetAsync<ApiResponse<List<TransactionItem>>>("transactions/history");

                await Task.WhenAll(profileTask, historyTask);

                var profile = await profileTask;
                if (profile != null && profile.Success && profile.Data != null)
                {
                    SessionManager.Instance.Balance = profile.Data.Balance;
                    SessionManager.Instance.Email = profile.Data.Email;
                    SessionManager.Instance.PhoneNumber = profile.Data.PhoneNumber;
                    SessionManager.Instance.CreatedAt = profile.Data.CreatedAt;
                    UpdateBalanceDisplay();
                }

                var history = await historyTask;
                if (history != null && history.Success && history.Data != null)
                {
                    _allTransactions = history.Data;
                    CalculateCashFlow();
                    FilterTransactions();
                }

                lblLastSync.Text = $"Synced at {DateTime.Now:HH:mm:ss} (Live)";
            }
            catch (Exception ex)
            {
                if (!isBackgroundPoll)
                {
                    lblLastSync.Text = $"Sync note: {ex.Message}";
                }
            }
        }

        private void CalculateCashFlow()
        {
            decimal inflow = _allTransactions
                .Where(t => t.Type == TransactionType.Deposit || t.Type == TransactionType.TransferIn)
                .Sum(t => t.Amount);

            decimal outflow = _allTransactions
                .Where(t => t.Type == TransactionType.Withdraw || t.Type == TransactionType.TransferOut)
                .Sum(t => t.Amount);

            lblInflow.Text = $"+৳{inflow:N2}";
            lblOutflow.Text = $"-৳{outflow:N2}";
        }

        private void FilterTransactions()
        {
            var selectedIndex = cmbFilter.SelectedIndex;
            var filter = selectedIndex >= 0 ? cmbFilter.Items[selectedIndex]?.ToString() ?? "All Transactions" : "All Transactions";

            var filtered = filter switch
            {
                "Deposits" => _allTransactions.Where(t => t.Type == TransactionType.Deposit).ToList(),
                "Withdrawals" => _allTransactions.Where(t => t.Type == TransactionType.Withdraw).ToList(),
                "Transfers Sent" => _allTransactions.Where(t => t.Type == TransactionType.TransferOut).ToList(),
                "Transfers Received" => _allTransactions.Where(t => t.Type == TransactionType.TransferIn).ToList(),
                _ => _allTransactions
            };

            // Avoid flickering by suspending layout
            dgvTransactions.SuspendLayout();
            dgvTransactions.Rows.Clear();
            foreach (var item in filtered)
            {
                dgvTransactions.Rows.Add(item.FormattedDate, item.TypeDescription, item.FormattedAmount, item.Status);
            }
            dgvTransactions.ResumeLayout();
        }

        private void BtnCopyAccount_Click(object? sender, EventArgs e)
        {
            if (!string.IsNullOrEmpty(SessionManager.Instance.AccountNumber))
            {
                Clipboard.SetText(SessionManager.Instance.AccountNumber);
                btnCopyAccount.Text = "Copied!";
                btnCopyAccount.BackColor = Color.FromArgb(220, 252, 231);
                btnCopyAccount.ForeColor = Color.FromArgb(22, 101, 52);

                var timer = new System.Windows.Forms.Timer { Interval = 2000 };
                timer.Tick += (s, ev) =>
                {
                    btnCopyAccount.Text = "Copy";
                    btnCopyAccount.BackColor = Color.FromArgb(241, 245, 249);
                    btnCopyAccount.ForeColor = Color.FromArgb(71, 85, 105);
                    timer.Stop();
                    timer.Dispose();
                };
                timer.Start();
            }
        }

        private async void BtnDeposit_Click(object? sender, EventArgs e)
        {
            using var dlg = new DepositDialog();
            if (dlg.ShowDialog(this) == DialogResult.OK && dlg.DepositAmount > 0)
            {
                try
                {
                    var res = await ApiClient.PostAsync<ApiResponse<decimal>>("transactions/deposit", new { amount = dlg.DepositAmount });
                    if (res != null && res.Success)
                    {
                        SessionManager.Instance.Balance = res.Data;
                        UpdateBalanceDisplay();
                        MessageBox.Show($"Deposit of ৳{dlg.DepositAmount:N2} successfully credited to your checking account.", "Deposit Successful", MessageBoxButtons.OK, MessageBoxIcon.Information);
                        await RefreshBalanceAndHistoryAsync(isBackgroundPoll: false);
                    }
                }
                catch (ApiException ex)
                {
                    MessageBox.Show(ex.Message, "Deposit Failed", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            }
        }

        private async void BtnTransfer_Click(object? sender, EventArgs e)
        {
            using var dlg = new TransferDialog();
            if (dlg.ShowDialog(this) == DialogResult.OK && dlg.TransferAmount > 0 && !string.IsNullOrWhiteSpace(dlg.RecipientAccount))
            {
                try
                {
                    var res = await ApiClient.PostAsync<ApiResponse<decimal>>("transactions/transfer", new
                    {
                        recipientAccountNumber = dlg.RecipientAccount.Trim(),
                        amount = dlg.TransferAmount
                    });

                    if (res != null && res.Success)
                    {
                        SessionManager.Instance.Balance = res.Data;
                        UpdateBalanceDisplay();
                        MessageBox.Show(res.Message, "Transfer Completed", MessageBoxButtons.OK, MessageBoxIcon.Information);
                        await RefreshBalanceAndHistoryAsync(isBackgroundPoll: false);
                    }
                }
                catch (ApiException ex)
                {
                    MessageBox.Show(ex.Message, "Transfer Failed", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            }
        }

        private async void BtnWithdraw_Click(object? sender, EventArgs e)
        {
            using var dlg = new WithdrawDialog();
            if (dlg.ShowDialog(this) == DialogResult.OK && dlg.WithdrawAmount > 0)
            {
                try
                {
                    var res = await ApiClient.PostAsync<ApiResponse<decimal>>("transactions/withdraw", new { amount = dlg.WithdrawAmount });
                    if (res != null && res.Success)
                    {
                        SessionManager.Instance.Balance = res.Data;
                        UpdateBalanceDisplay();
                        MessageBox.Show($"ATM Withdrawal of ৳{dlg.WithdrawAmount:N2} successfully processed.", "Withdrawal Successful", MessageBoxButtons.OK, MessageBoxIcon.Information);
                        await RefreshBalanceAndHistoryAsync(isBackgroundPoll: false);
                    }
                }
                catch (ApiException ex)
                {
                    MessageBox.Show(ex.Message, "Withdrawal Failed", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            }
        }

        private async void BtnPayBill_Click(object? sender, EventArgs e)
        {
            using var dlg = new BillPayDialog();
            if (dlg.ShowDialog(this) == DialogResult.OK && dlg.BillAmount > 0)
            {
                try
                {
                    var res = await ApiClient.PostAsync<ApiResponse<decimal>>("transactions/pay-bill", new
                    {
                        billerName = dlg.BillerName,
                        billType = dlg.BillType,
                        referenceNumber = dlg.ReferenceNumber,
                        amount = dlg.BillAmount
                    });

                    if (res != null && res.Success)
                    {
                        SessionManager.Instance.Balance = res.Data;
                        UpdateBalanceDisplay();
                        MessageBox.Show(res.Message, "Bill Paid Successfully", MessageBoxButtons.OK, MessageBoxIcon.Information);
                        await RefreshBalanceAndHistoryAsync(isBackgroundPoll: false);
                    }
                }
                catch (ApiException ex)
                {
                    MessageBox.Show(ex.Message, "Bill Payment Failed", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            }
        }

        private void BtnExportStatement_Click(object? sender, EventArgs e)
        {
            using var sfd = new SaveFileDialog
            {
                Filter = "Text Bank Statement (*.txt)|*.txt|CSV Spreadsheet (*.csv)|*.csv",
                FileName = $"SmartBank_Statement_{DateTime.Now:yyyyMMdd_HHmm}.txt"
            };

            if (sfd.ShowDialog() == DialogResult.OK)
            {
                try
                {
                    var sb = new System.Text.StringBuilder();
                    var isCsv = sfd.FileName.EndsWith(".csv", StringComparison.OrdinalIgnoreCase);

                    if (isCsv)
                    {
                        sb.AppendLine("Date,Type,Amount,Status");
                        foreach (var item in _allTransactions)
                        {
                            sb.AppendLine($"\"{item.FormattedDate}\",\"{item.TypeDescription}\",\"{item.FormattedAmount}\",\"{item.Status}\"");
                        }
                    }
                    else
                    {
                        sb.AppendLine("================================================================================");
                        sb.AppendLine("                       SMARTBANK OFFICIAL ACCOUNT STATEMENT                     ");
                        sb.AppendLine("================================================================================");
                        sb.AppendLine($"Account Holder : {SessionManager.Instance.FullName}");
                        sb.AppendLine($"Account Number : {SessionManager.Instance.AccountNumber}");
                        sb.AppendLine($"Generated On   : {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                        sb.AppendLine($"Current Balance: ৳{SessionManager.Instance.Balance:N2}");
                        sb.AppendLine("--------------------------------------------------------------------------------");
                        sb.AppendLine(string.Format("{0,-20} {1,-25} {2,-18} {3,-12}", "DATE & TIME", "TYPE", "AMOUNT", "STATUS"));
                        sb.AppendLine("--------------------------------------------------------------------------------");
                        foreach (var item in _allTransactions)
                        {
                            sb.AppendLine(string.Format("{0,-20} {1,-25} {2,-18} {3,-12}", item.FormattedDate, item.TypeDescription, item.FormattedAmount, item.Status));
                        }
                        sb.AppendLine("================================================================================");
                        sb.AppendLine("           Thank you for banking with SmartBank Premier Wealth Management       ");
                        sb.AppendLine("================================================================================");
                    }

                    File.WriteAllText(sfd.FileName, sb.ToString());
                    MessageBox.Show("Bank Statement exported successfully!", "Export Complete", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Export error: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

        private async void BtnChangePassword_Click(object? sender, EventArgs e)
        {
            using var dlg = new ChangePasswordDialog();
            if (dlg.ShowDialog(this) == DialogResult.OK)
            {
                try
                {
                    var res = await ApiClient.PostAsync<ApiResponse<bool>>("auth/change-password", new
                    {
                        currentPassword = dlg.CurrentPassword,
                        newPassword = dlg.NewPassword,
                        confirmNewPassword = dlg.ConfirmPassword
                    });

                    if (res != null && res.Success)
                    {
                        MessageBox.Show("Your password has been updated securely.", "Password Updated", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    }
                }
                catch (ApiException ex)
                {
                    MessageBox.Show(ex.Message, "Change Password Failed", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            }
        }

        private async void BtnLogout_Click(object? sender, EventArgs e)
        {
            _liveSyncTimer?.Stop();
            var auth = new AuthService();
            await auth.LogoutAsync();
            this.Close();
        }
    }
}
