using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using SmartBank.Client.Exceptions;
using SmartBank.Client.Models.Common;
using SmartBank.Client.Security;
using SmartBank.Client.Services;

namespace SmartBank.Client.Forms
{
    public class AdminDashboardForm : Form
    {
        private DataGridView dgvUsers = null!;
        private TextBox txtSearch = null!;
        private Button btnClearSearch = null!;
        private Button btnEditUser = null!;
        private Button btnToggleStatus = null!;
        private Button btnSuspendUser = null!;
        private Button btnUnlockUser = null!;
        private Button btnRefresh = null!;
        private Button btnLogout = null!;
        private Label lblTotalUsers = null!;
        private Label lblTotalBankDeposits = null!;
        private Label lblActiveAccounts = null!;
        private Label lblLockedUsers = null!;
        private Panel pnlUserBadge = null!;
        private Label lblUserAvatar = null!;
        private Label lblUserName = null!;
        private Label lblUserTier = null!;
        private Label lblNoData = null!;

        private List<AdminUserItem> _allUsers = new();
        private bool _suppressSearchFilter = false;
        private string _activeFilterCategory = "ALL"; // ALL, LOCKED_SUSPENDED, ACTIVE, TOP_BALANCE

        public class AdminUserItem
        {
            public int Id { get; set; }
            public string Username { get; set; } = string.Empty;
            public string FullName { get; set; } = string.Empty;
            public string Email { get; set; } = string.Empty;
            public string? PhoneNumber { get; set; }
            public string? AccountNumber { get; set; }
            public decimal Balance { get; set; }
            public bool AccountActive { get; set; }
            public bool IsLocked { get; set; }
            public string Status { get; set; } = string.Empty;
            public string Role { get; set; } = string.Empty;

            public bool IsSuspended => Status.Equals("Suspended", StringComparison.OrdinalIgnoreCase);
            public string FormattedBalance => $"৳{Balance:N2}";
            public string AccountStatus => AccountActive ? "🟢 Active" : "🔴 Frozen";
            public string SecurityStatus => IsSuspended ? "🚫 Suspended" : (IsLocked ? "🔒 Locked" : "🛡️ Normal");
        }

        public AdminDashboardForm()
        {
            InitializeComponents();
            _ = LoadUsersAsync();
        }

        private void InitializeComponents()
        {
            this.Text = "SmartBank — Executive Administration & Risk Management";
            this.ClientSize = new Size(1060, 730);
            this.StartPosition = FormStartPosition.CenterScreen;
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.BackColor = Color.FromArgb(243, 246, 251);
            this.Font = new Font("Segoe UI", 9F, FontStyle.Regular, GraphicsUnit.Point);

            // 1. HEADER (Fixed layout, clean logo + motto, bKash-style admin profile badge)
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
                Text = "Executive Administration & Risk Management",
                ForeColor = Color.FromArgb(148, 163, 184),
                Font = new Font("Segoe UI", 8.5F),
                Location = new Point(26, 50),
                AutoSize = true
            };

            // bKash-style Admin Profile Badge Card
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
            pnlUserBadge.Click += (s, e) => OpenAdminProfileDialog();

            var initials = "AD";
            var sess = SessionManager.Instance;
            if (!string.IsNullOrEmpty(sess.FullName))
            {
                var parts = sess.FullName.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                initials = parts.Length >= 2 ? $"{parts[0][0]}{parts[1][0]}".ToUpper() : sess.FullName.Substring(0, Math.Min(2, sess.FullName.Length)).ToUpper();
            }

            lblUserAvatar = new Label
            {
                Text = initials,
                Font = new Font("Segoe UI", 11F, FontStyle.Bold),
                ForeColor = Color.White,
                BackColor = Color.FromArgb(225, 29, 72), // Rose / Red for Admin
                Size = new Size(40, 40),
                Location = new Point(8, 8),
                TextAlign = ContentAlignment.MiddleCenter,
                Cursor = Cursors.Hand
            };
            lblUserAvatar.Click += (s, e) => OpenAdminProfileDialog();

            lblUserName = new Label
            {
                Text = string.IsNullOrEmpty(sess.FullName) ? sess.Username : sess.FullName,
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 10.5F, FontStyle.Bold),
                Location = new Point(56, 8),
                AutoSize = true,
                Cursor = Cursors.Hand
            };
            lblUserName.Click += (s, e) => OpenAdminProfileDialog();

            lblUserTier = new Label
            {
                Text = "🛡️ System Administrator • Details ➔",
                ForeColor = Color.FromArgb(248, 113, 113),
                Font = new Font("Segoe UI", 8F, FontStyle.Bold),
                Location = new Point(56, 30),
                AutoSize = true,
                Cursor = Cursors.Hand
            };
            lblUserTier.Click += (s, e) => OpenAdminProfileDialog();

            pnlUserBadge.Controls.AddRange(new Control[] { lblUserAvatar, lblUserName, lblUserTier });

            var btnManageLoans = new Button
            {
                Text = "💰 Manage Loans",
                Font = new Font("Segoe UI", 9.5F, FontStyle.Bold),
                ForeColor = Color.White,
                BackColor = Color.FromArgb(16, 185, 129),
                FlatStyle = FlatStyle.Flat,
                Size = new Size(140, 42),
                Location = new Point(775, 21),
                Cursor = Cursors.Hand
            };
            btnManageLoans.FlatAppearance.BorderSize = 0;
            btnManageLoans.Click += (s, e) =>
            {
                using var loanDialog = new AdminLoanReviewDialog();
                loanDialog.ShowDialog(this);
            };

            btnLogout = new Button
            {
                Text = "Logout",
                Font = new Font("Segoe UI", 9.5F, FontStyle.Bold),
                ForeColor = Color.White,
                BackColor = Color.FromArgb(225, 29, 72),
                FlatStyle = FlatStyle.Flat,
                Size = new Size(110, 42),
                Location = new Point(925, 21),
                Cursor = Cursors.Hand
            };
            btnLogout.FlatAppearance.BorderSize = 0;
            btnLogout.Click += BtnLogout_Click;

            pnlHeader.Controls.AddRange(new Control[] { lblLogo, lblSubtitle, pnlUserBadge, btnManageLoans, btnLogout });

            // 2. INTERACTIVE METRICS CARDS
            var pnlMetrics = new Panel { Location = new Point(24, 100), Size = new Size(1012, 90), BackColor = Color.Transparent };

            var card1 = CreateMetricCard("👥 Total Registered Users", "0", Color.FromArgb(37, 99, 235), new Point(0, 0), out lblTotalUsers, () =>
            {
                _activeFilterCategory = "ALL";
                txtSearch.Clear();
                FilterUsers();
            });

            var card2 = CreateMetricCard("💰 Total Bank Deposits", "৳0.00", Color.FromArgb(5, 150, 105), new Point(260, 0), out lblTotalBankDeposits, () =>
            {
                _activeFilterCategory = "TOP_BALANCE";
                txtSearch.Clear();
                FilterUsers();
            });

            var card3 = CreateMetricCard("🟢 Active Bank Accounts", "0", Color.FromArgb(16, 185, 129), new Point(520, 0), out lblActiveAccounts, () =>
            {
                _activeFilterCategory = "ACTIVE";
                txtSearch.Clear();
                FilterUsers();
            });

            var card4 = CreateMetricCard("🔒 Locked / Suspended (Click to Filter)", "0", Color.FromArgb(225, 29, 72), new Point(780, 0), out lblLockedUsers, () =>
            {
                _activeFilterCategory = "LOCKED_SUSPENDED";
                txtSearch.Clear();
                FilterUsers();
            });

            pnlMetrics.Controls.AddRange(new Control[] { card1, card2, card3, card4 });

            // 3. ACTIONS & SEARCH TOOLBAR (Pixel-perfect vertical alignment)
            var pnlToolbar = new Panel { Location = new Point(24, 202), Size = new Size(1012, 46), BackColor = Color.Transparent };

            var lblSearch = new Label
            {
                Text = "🔍 Account #:",
                Font = new Font("Segoe UI", 9.5F, FontStyle.Bold),
                ForeColor = Color.FromArgb(71, 85, 105),
                Location = new Point(0, 10),
                AutoSize = true
            };

            txtSearch = new TextBox
            {
                Location = new Point(96, 6),
                Size = new Size(130, 28),
                Font = new Font("Segoe UI", 10.5F),
                PlaceholderText = "Enter digits..."
            };
            txtSearch.TextChanged += TxtSearch_TextChanged;

            btnClearSearch = new Button
            {
                Text = "✕",
                Font = new Font("Segoe UI", 9F, FontStyle.Bold),
                ForeColor = Color.FromArgb(100, 116, 139),
                BackColor = Color.FromArgb(241, 245, 249),
                FlatStyle = FlatStyle.Flat,
                Size = new Size(28, 28),
                Location = new Point(232, 6),
                Cursor = Cursors.Hand
            };
            btnClearSearch.FlatAppearance.BorderColor = Color.FromArgb(203, 213, 225);
            btnClearSearch.Click += (s, e) =>
            {
                _activeFilterCategory = "ALL";
                txtSearch.Clear();
                FilterUsers();
                txtSearch.Focus();
            };

            btnEditUser = new Button
            {
                Text = "✏️ Edit User",
                Font = new Font("Segoe UI", 9F, FontStyle.Bold),
                ForeColor = Color.White,
                BackColor = Color.FromArgb(203, 213, 225),
                FlatStyle = FlatStyle.Flat,
                Size = new Size(130, 34),
                Location = new Point(270, 5),
                Enabled = false,
                Cursor = Cursors.Default
            };
            btnEditUser.FlatAppearance.BorderSize = 0;
            btnEditUser.Click += BtnEditUser_Click;

            btnToggleStatus = new Button
            {
                Text = "❄️ Freeze Account",
                Font = new Font("Segoe UI", 9F, FontStyle.Bold),
                ForeColor = Color.White,
                BackColor = Color.FromArgb(203, 213, 225),
                FlatStyle = FlatStyle.Flat,
                Size = new Size(155, 34),
                Location = new Point(410, 5),
                Enabled = false,
                Cursor = Cursors.Default
            };
            btnToggleStatus.FlatAppearance.BorderSize = 0;
            btnToggleStatus.Click += BtnToggleStatus_Click;

            btnSuspendUser = new Button
            {
                Text = "🚫 Suspend User",
                Font = new Font("Segoe UI", 9F, FontStyle.Bold),
                ForeColor = Color.White,
                BackColor = Color.FromArgb(203, 213, 225),
                FlatStyle = FlatStyle.Flat,
                Size = new Size(155, 34),
                Location = new Point(575, 5),
                Enabled = false,
                Cursor = Cursors.Default
            };
            btnSuspendUser.FlatAppearance.BorderSize = 0;
            btnSuspendUser.Click += BtnSuspendUser_Click;

            btnUnlockUser = new Button
            {
                Text = "🔓 Unlock Logins",
                Font = new Font("Segoe UI", 9F, FontStyle.Bold),
                ForeColor = Color.White,
                BackColor = Color.FromArgb(203, 213, 225),
                FlatStyle = FlatStyle.Flat,
                Size = new Size(150, 34),
                Location = new Point(740, 5),
                Enabled = false,
                Cursor = Cursors.Default
            };
            btnUnlockUser.FlatAppearance.BorderSize = 0;
            btnUnlockUser.Click += BtnUnlockUser_Click;

            btnRefresh = new Button
            {
                Text = "🔄 Refresh",
                Font = new Font("Segoe UI", 9F, FontStyle.Bold),
                ForeColor = Color.FromArgb(37, 99, 235),
                BackColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Size = new Size(95, 34),
                Location = new Point(900, 5),
                Cursor = Cursors.Hand
            };
            btnRefresh.FlatAppearance.BorderColor = Color.FromArgb(203, 213, 225);
            btnRefresh.Click += async (s, e) =>
            {
                _activeFilterCategory = "ALL";
                await LoadUsersAsync();
            };

            pnlToolbar.Controls.AddRange(new Control[] { lblSearch, txtSearch, btnClearSearch, btnEditUser, btnToggleStatus, btnSuspendUser, btnUnlockUser, btnRefresh });

            // 4. USERS DATAGRIDVIEW
            dgvUsers = new DataGridView
            {
                Location = new Point(24, 258),
                Size = new Size(1012, 442),
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
            dgvUsers.EnableHeadersVisualStyles = false;
            dgvUsers.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(241, 245, 249);
            dgvUsers.ColumnHeadersDefaultCellStyle.ForeColor = Color.FromArgb(30, 41, 59);
            dgvUsers.ColumnHeadersDefaultCellStyle.Font = new Font("Segoe UI", 9F, FontStyle.Bold);
            dgvUsers.ColumnHeadersHeight = 36;
            dgvUsers.DefaultCellStyle.Font = new Font("Segoe UI", 9F);
            dgvUsers.DefaultCellStyle.SelectionBackColor = Color.FromArgb(239, 246, 255);
            dgvUsers.DefaultCellStyle.SelectionForeColor = Color.FromArgb(15, 23, 42);
            dgvUsers.AlternatingRowsDefaultCellStyle.BackColor = Color.FromArgb(248, 250, 252);

            dgvUsers.Columns.Add(new DataGridViewTextBoxColumn { Name = "Id", HeaderText = "User ID", FillWeight = 10 });
            dgvUsers.Columns.Add(new DataGridViewTextBoxColumn { Name = "FullName", HeaderText = "Full Name", FillWeight = 22 });
            dgvUsers.Columns.Add(new DataGridViewTextBoxColumn { Name = "Username", HeaderText = "Username", FillWeight = 16 });
            dgvUsers.Columns.Add(new DataGridViewTextBoxColumn { Name = "Email", HeaderText = "Email Address", FillWeight = 24 });
            dgvUsers.Columns.Add(new DataGridViewTextBoxColumn { Name = "AccountNumber", HeaderText = "Account #", FillWeight = 18 });
            dgvUsers.Columns.Add(new DataGridViewTextBoxColumn { Name = "FormattedBalance", HeaderText = "Balance", FillWeight = 16 });
            dgvUsers.Columns.Add(new DataGridViewTextBoxColumn { Name = "AccountStatus", HeaderText = "Account", FillWeight = 14 });
            dgvUsers.Columns.Add(new DataGridViewTextBoxColumn { Name = "SecurityStatus", HeaderText = "Security", FillWeight = 16 });

            dgvUsers.SelectionChanged += (s, e) => UpdateActionButtonsState();
            dgvUsers.CellClick += DgvUsers_CellClick;
            dgvUsers.CellDoubleClick += DgvUsers_CellDoubleClick;

            // Placeholder for empty results / validation notices
            lblNoData = new Label
            {
                Text = "No account found matching the specified criteria.",
                Font = new Font("Segoe UI", 11F, FontStyle.Italic),
                ForeColor = Color.FromArgb(148, 163, 184),
                AutoSize = false,
                TextAlign = ContentAlignment.MiddleCenter,
                Size = new Size(600, 40),
                Location = new Point(230, 420),
                Visible = false
            };

            this.Controls.AddRange(new Control[] { lblNoData, dgvUsers, pnlToolbar, pnlMetrics, pnlHeader });
        }

        private static void DrawBorder(Graphics g, Rectangle rect, Color color)
        {
            using var pen = new Pen(color, 1);
            g.DrawRectangle(pen, rect.X, rect.Y, rect.Width - 1, rect.Height - 1);
        }

        private void OpenAdminProfileDialog()
        {
            using var dlg = new UserProfileDialog();
            dlg.ShowDialog(this);
            if (dlg.RequestedLogout)
            {
                BtnLogout_Click(this, EventArgs.Empty);
            }
            else if (dlg.HasChangesSaved)
            {
                UpdateAdminBadge();
            }
        }

        private void UpdateAdminBadge()
        {
            var sess = SessionManager.Instance;
            lblUserName.Text = string.IsNullOrEmpty(sess.FullName) ? sess.Username : sess.FullName;
            var initials = "AD";
            if (!string.IsNullOrEmpty(sess.FullName))
            {
                var parts = sess.FullName.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                initials = parts.Length >= 2 ? $"{parts[0][0]}{parts[1][0]}".ToUpper() : sess.FullName.Substring(0, Math.Min(2, sess.FullName.Length)).ToUpper();
            }
            lblUserAvatar.Text = initials;
        }

        private static Panel CreateMetricCard(string title, string initialValue, Color accentColor, Point location, out Label valueLabel, Action? onClick = null)
        {
            var pnl = new Panel
            {
                Location = location,
                Size = new Size(242, 84),
                BackColor = Color.White,
                Cursor = onClick != null ? Cursors.Hand : Cursors.Default
            };
            pnl.Paint += (s, e) =>
            {
                using var pen = new Pen(Color.FromArgb(226, 232, 240), 1);
                e.Graphics.DrawRectangle(pen, 0, 0, pnl.Width - 1, pnl.Height - 1);
            };

            var lblT = new Label { Text = title, Font = new Font("Segoe UI", 8.5F, FontStyle.Bold), ForeColor = Color.FromArgb(100, 116, 139), Location = new Point(14, 12), AutoSize = true, Cursor = pnl.Cursor };
            valueLabel = new Label { Text = initialValue, Font = new Font("Segoe UI", 16F, FontStyle.Bold), ForeColor = accentColor, Location = new Point(14, 34), AutoSize = true, Cursor = pnl.Cursor };

            if (onClick != null)
            {
                pnl.Click += (s, e) => onClick();
                lblT.Click += (s, e) => onClick();
                valueLabel.Click += (s, e) => onClick();
                pnl.MouseEnter += (s, e) => pnl.BackColor = Color.FromArgb(248, 250, 252);
                pnl.MouseLeave += (s, e) => pnl.BackColor = Color.White;
            }

            pnl.Controls.AddRange(new Control[] { lblT, valueLabel });
            return pnl;
        }

        private async Task LoadUsersAsync()
        {
            try
            {
                var res = await ApiClient.GetAsync<ApiResponse<List<AdminUserItem>>>("admin/users");
                if (res != null && res.Success && res.Data != null)
                {
                    _allUsers = res.Data;
                    UpdateMetrics();
                    FilterUsers();
                }
            }
            catch (ApiException ex)
            {
                MessageBox.Show(ex.Message, "Error Loading Users", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void UpdateMetrics()
        {
            lblTotalUsers.Text = _allUsers.Count.ToString();
            decimal totalDeposits = _allUsers.Sum(u => u.Balance);
            lblTotalBankDeposits.Text = $"৳{totalDeposits:N2}";
            lblActiveAccounts.Text = _allUsers.Count(u => u.AccountActive).ToString();
            lblLockedUsers.Text = _allUsers.Count(u => u.IsLocked || u.IsSuspended).ToString();
        }

        private void TxtSearch_TextChanged(object? sender, EventArgs e)
        {
            if (_suppressSearchFilter) return;
            FilterUsers();
        }

        private void DgvUsers_CellClick(object? sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex >= 0 && dgvUsers.Rows[e.RowIndex].Tag is AdminUserItem item)
            {
                if (!string.IsNullOrEmpty(item.AccountNumber) && item.AccountNumber != "N/A")
                {
                    _suppressSearchFilter = true;
                    txtSearch.Text = item.AccountNumber;
                    _suppressSearchFilter = false;
                }
                UpdateActionButtonsState();
            }
        }

        private void FilterUsers()
        {
            dgvUsers.Rows.Clear();
            var search = txtSearch.Text.Trim();

            // Validate format: must contain digits only if entered
            if (!string.IsNullOrEmpty(search) && !search.All(char.IsDigit))
            {
                lblNoData.Text = "⚠️ Invalid format: Account number must contain digits only.";
                lblNoData.ForeColor = Color.FromArgb(225, 29, 72);
                lblNoData.Visible = true;
                UpdateActionButtonsState();
                return;
            }

            IEnumerable<AdminUserItem> query = _allUsers;

            // Apply metric card category filter
            switch (_activeFilterCategory)
            {
                case "LOCKED_SUSPENDED":
                    query = query.Where(u => u.IsLocked || u.IsSuspended);
                    break;
                case "ACTIVE":
                    query = query.Where(u => u.AccountActive && !u.IsSuspended);
                    break;
                case "TOP_BALANCE":
                    query = query.OrderByDescending(u => u.Balance);
                    break;
                default:
                    break;
            }

            // Apply Account Number search if typed
            if (!string.IsNullOrEmpty(search))
            {
                query = query.Where(u => u.AccountNumber != null && u.AccountNumber.Contains(search));
            }

            var filtered = query.ToList();

            foreach (var u in filtered)
            {
                var idx = dgvUsers.Rows.Add(u.Id, u.FullName, u.Username, u.Email, u.AccountNumber ?? "N/A", u.FormattedBalance, u.AccountStatus, u.SecurityStatus);
                dgvUsers.Rows[idx].Tag = u;
            }

            if (filtered.Count == 0)
            {
                lblNoData.Text = !string.IsNullOrEmpty(search)
                    ? $"⚠️ No account found matching '{search}'."
                    : (_activeFilterCategory == "LOCKED_SUSPENDED" ? "🟢 No locked or suspended users currently in system." : "No registered account records found.");
                lblNoData.ForeColor = Color.FromArgb(148, 163, 184);
                lblNoData.Visible = true;
            }
            else
            {
                lblNoData.Visible = false;
            }

            UpdateActionButtonsState();
        }

        private void UpdateActionButtonsState()
        {
            bool hasSelection = dgvUsers.SelectedRows.Count > 0 && dgvUsers.SelectedRows[0].Tag is AdminUserItem;

            if (hasSelection)
            {
                var selected = (AdminUserItem)dgvUsers.SelectedRows[0].Tag!;
                bool isTargetAdmin = selected.Role.Equals("Admin", StringComparison.OrdinalIgnoreCase);

                // Edit User Button
                btnEditUser.Enabled = true;
                btnEditUser.BackColor = Color.FromArgb(37, 99, 235);
                btnEditUser.Cursor = Cursors.Hand;

                // 1. Freeze / Unfreeze Account Button
                btnToggleStatus.Enabled = !isTargetAdmin;
                btnToggleStatus.BackColor = selected.AccountActive ? Color.FromArgb(217, 119, 6) : Color.FromArgb(16, 185, 129);
                btnToggleStatus.Text = selected.AccountActive ? "❄️ Freeze Account" : "🟢 Unfreeze Account";
                btnToggleStatus.Cursor = !isTargetAdmin ? Cursors.Hand : Cursors.Default;

                // 2. Suspend / Unsuspend User Button
                btnSuspendUser.Enabled = !isTargetAdmin;
                btnSuspendUser.BackColor = selected.IsSuspended ? Color.FromArgb(16, 185, 129) : Color.FromArgb(225, 29, 72);
                btnSuspendUser.Text = selected.IsSuspended ? "🟢 Lift Suspension" : "🚫 Suspend User";
                btnSuspendUser.Cursor = !isTargetAdmin ? Cursors.Hand : Cursors.Default;

                // 3. Unlock Button
                btnUnlockUser.Enabled = selected.IsLocked && !isTargetAdmin;
                btnUnlockUser.BackColor = selected.IsLocked ? Color.FromArgb(225, 29, 72) : Color.FromArgb(203, 213, 225);
                btnUnlockUser.Text = selected.IsLocked ? "🔓 Unlock User" : "🔒 Not Locked";
                btnUnlockUser.Cursor = (selected.IsLocked && !isTargetAdmin) ? Cursors.Hand : Cursors.Default;
            }
            else
            {
                // No rows selected or empty search result
                btnEditUser.Enabled = false;
                btnEditUser.BackColor = Color.FromArgb(203, 213, 225);
                btnEditUser.Cursor = Cursors.Default;

                btnToggleStatus.Enabled = false;
                btnToggleStatus.BackColor = Color.FromArgb(203, 213, 225);
                btnToggleStatus.Text = "❄️ Freeze Account";
                btnToggleStatus.Cursor = Cursors.Default;

                btnSuspendUser.Enabled = false;
                btnSuspendUser.BackColor = Color.FromArgb(203, 213, 225);
                btnSuspendUser.Text = "🚫 Suspend User";
                btnSuspendUser.Cursor = Cursors.Default;

                btnUnlockUser.Enabled = false;
                btnUnlockUser.BackColor = Color.FromArgb(203, 213, 225);
                btnUnlockUser.Text = "🔓 Unlock Logins";
                btnUnlockUser.Cursor = Cursors.Default;
            }
        }

        private async void BtnEditUser_Click(object? sender, EventArgs e)
        {
            if (dgvUsers.SelectedRows.Count == 0 || dgvUsers.SelectedRows[0].Tag is not AdminUserItem selected)
            {
                MessageBox.Show("Please select a user from the table to edit.", "Selection Required", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            using var dlg = new AdminEditUserDialog(selected);
            if (dlg.ShowDialog(this) == DialogResult.OK)
            {
                await LoadUsersAsync();
            }
        }

        private void DgvUsers_CellDoubleClick(object? sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex >= 0 && dgvUsers.Rows[e.RowIndex].Tag is AdminUserItem)
            {
                BtnEditUser_Click(sender, EventArgs.Empty);
            }
        }

        private async void BtnToggleStatus_Click(object? sender, EventArgs e)
        {
            if (dgvUsers.SelectedRows.Count == 0 || dgvUsers.SelectedRows[0].Tag is not AdminUserItem selected || string.IsNullOrEmpty(selected.AccountNumber))
            {
                MessageBox.Show("No valid account is currently selected. Please choose a valid account from the table first.", "Selection Required", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            try
            {
                var res = await ApiClient.PostAsync<ApiResponse<bool>>($"admin/toggle-account?userId={selected.Id}");
                if (res != null && res.Success)
                {
                    MessageBox.Show(res.Message, "Account Status Updated", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    await LoadUsersAsync();
                }
            }
            catch (ApiException ex)
            {
                MessageBox.Show(ex.Message, "Operation Failed", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private async void BtnSuspendUser_Click(object? sender, EventArgs e)
        {
            if (dgvUsers.SelectedRows.Count == 0 || dgvUsers.SelectedRows[0].Tag is not AdminUserItem selected)
            {
                MessageBox.Show("No valid user is currently selected.", "Selection Required", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            if (selected.IsSuspended)
            {
                // Lift suspension
                var confirm = MessageBox.Show(
                    $"Lift administrative suspension for '{selected.FullName}' (@{selected.Username}) and restore active banking access?",
                    "Confirm Lift Suspension",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Question);

                if (confirm != DialogResult.Yes) return;

                try
                {
                    var res = await ApiClient.PostAsync<ApiResponse<bool>>($"admin/unsuspend-user?userId={selected.Id}");
                    if (res != null && res.Success)
                    {
                        MessageBox.Show(res.Message, "Suspension Lifted", MessageBoxButtons.OK, MessageBoxIcon.Information);
                        await LoadUsersAsync();
                    }
                }
                catch (ApiException ex)
                {
                    MessageBox.Show(ex.Message, "Operation Failed", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            }
            else
            {
                // Open Duration Suspension Dialog
                using var dlg = new SuspendDialog(selected.FullName, selected.Username, selected.AccountNumber ?? "N/A", selected.Balance);
                if (dlg.ShowDialog(this) == DialogResult.OK)
                {
                    try
                    {
                        var reqBody = new
                        {
                            userId = selected.Id,
                            durationHours = dlg.SelectedDurationHours,
                            reason = dlg.Reason
                        };

                        var res = await ApiClient.PostAsync<ApiResponse<bool>>("admin/suspend-user", reqBody);
                        if (res != null && res.Success)
                        {
                            MessageBox.Show(res.Message, "Administrative Suspension Enforced", MessageBoxButtons.OK, MessageBoxIcon.Information);
                            await LoadUsersAsync();
                        }
                    }
                    catch (ApiException ex)
                    {
                        MessageBox.Show(ex.Message, "Suspension Failed", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    }
                }
            }
        }

        private async void BtnUnlockUser_Click(object? sender, EventArgs e)
        {
            if (dgvUsers.SelectedRows.Count == 0 || dgvUsers.SelectedRows[0].Tag is not AdminUserItem selected || string.IsNullOrEmpty(selected.AccountNumber))
            {
                MessageBox.Show("No valid account is currently selected. Please choose a valid account from the table first.", "Selection Required", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            try
            {
                var res = await ApiClient.PostAsync<ApiResponse<bool>>($"admin/unlock-user?userId={selected.Id}");
                if (res != null && res.Success)
                {
                    MessageBox.Show(res.Message, "User Account Unlocked", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    await LoadUsersAsync();
                }
            }
            catch (ApiException ex)
            {
                MessageBox.Show(ex.Message, "Operation Failed", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private async void BtnLogout_Click(object? sender, EventArgs e)
        {
            var auth = new AuthService();
            await auth.LogoutAsync();
            this.Close();
        }
    }
}
