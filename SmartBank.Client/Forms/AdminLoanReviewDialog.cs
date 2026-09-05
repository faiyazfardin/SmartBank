using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using SmartBank.Client.Models.Common;
using SmartBank.Client.Models.Loans;
using SmartBank.Client.Services;

namespace SmartBank.Client.Forms
{
    public class AdminLoanReviewDialog : Form
    {
        private DataGridView dgvLoans = null!;
        private ComboBox cmbStatusFilter = null!;
        private TextBox txtDetails = null!;
        private Button btnApprove = null!;
        private Button btnReject = null!;
        private Button btnRefresh = null!;
        private Label lblStats = null!;
        private List<LoanApplicationDto> _applications = new();

        public AdminLoanReviewDialog()
        {
            InitializeComponents();
            _ = LoadLoansAsync();
        }

        private void InitializeComponents()
        {
            this.Text = "SmartBank — Loan Underwriting & Governance Review";
            this.ClientSize = new Size(950, 680);
            this.StartPosition = FormStartPosition.CenterParent;
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.BackColor = Color.FromArgb(15, 23, 42); // Slate 900
            this.ForeColor = Color.White;
            this.Font = new Font("Segoe UI", 9F, FontStyle.Regular, GraphicsUnit.Point);

            // Header
            var pnlHeader = new Panel
            {
                Dock = DockStyle.Top,
                Height = 75,
                BackColor = Color.FromArgb(30, 41, 59),
                Padding = new Padding(20, 14, 20, 14)
            };

            var lblTitle = new Label
            {
                Text = "💰 Loan Applications Governance",
                Font = new Font("Segoe UI", 14F, FontStyle.Bold),
                ForeColor = Color.White,
                Location = new Point(20, 12),
                AutoSize = true
            };

            lblStats = new Label
            {
                Text = "Pending: 0 | Approved: 0 | Rejected: 0",
                Font = new Font("Segoe UI", 8.5F),
                ForeColor = Color.FromArgb(52, 211, 153),
                Location = new Point(22, 42),
                AutoSize = true
            };

            cmbStatusFilter = new ComboBox
            {
                Location = new Point(680, 22),
                Size = new Size(130, 30),
                DropDownStyle = ComboBoxStyle.DropDownList,
                BackColor = Color.FromArgb(15, 23, 42),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat
            };
            cmbStatusFilter.Items.AddRange(new object[] { "All Statuses", "Pending", "Approved", "Rejected" });
            cmbStatusFilter.SelectedIndex = 0;
            cmbStatusFilter.SelectedIndexChanged += async (s, e) => await LoadLoansAsync();

            btnRefresh = new Button
            {
                Text = "Refresh",
                Location = new Point(820, 22),
                Size = new Size(100, 30),
                BackColor = Color.FromArgb(51, 65, 85),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat
            };
            btnRefresh.Click += async (s, e) => await LoadLoansAsync();

            pnlHeader.Controls.AddRange(new Control[] { lblTitle, lblStats, cmbStatusFilter, btnRefresh });
            this.Controls.Add(pnlHeader);

            // DataGridView
            dgvLoans = new DataGridView
            {
                Location = new Point(20, 90),
                Size = new Size(910, 340),
                BackgroundColor = Color.FromArgb(30, 41, 59),
                ForeColor = Color.White,
                GridColor = Color.FromArgb(51, 65, 85),
                RowHeadersVisible = false,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                ReadOnly = true,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                MultiSelect = false,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                BorderStyle = BorderStyle.None
            };
            dgvLoans.DefaultCellStyle.BackColor = Color.FromArgb(30, 41, 59);
            dgvLoans.DefaultCellStyle.ForeColor = Color.White;
            dgvLoans.DefaultCellStyle.SelectionBackColor = Color.FromArgb(99, 102, 241);
            dgvLoans.DefaultCellStyle.SelectionForeColor = Color.White;
            dgvLoans.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(15, 23, 42);
            dgvLoans.ColumnHeadersDefaultCellStyle.ForeColor = Color.FromArgb(148, 163, 184);
            dgvLoans.ColumnHeadersDefaultCellStyle.Font = new Font("Segoe UI", 9F, FontStyle.Bold);
            dgvLoans.EnableHeadersVisualStyles = false;

            dgvLoans.Columns.Add("AppNumber", "Application #");
            dgvLoans.Columns.Add("Customer", "Customer");
            dgvLoans.Columns.Add("Account", "Account #");
            dgvLoans.Columns.Add("Type", "Type");
            dgvLoans.Columns.Add("Amount", "Requested (৳)");
            dgvLoans.Columns.Add("Score", "Score");
            dgvLoans.Columns.Add("Status", "Status");
            dgvLoans.Columns.Add("Date", "Submission Date");

            dgvLoans.SelectionChanged += DgvLoans_SelectionChanged;
            this.Controls.Add(dgvLoans);

            // Details Box
            var lblDetailsHeader = new Label
            {
                Text = "SELECTED APPLICATION DETAILS & CREDIT MEMO",
                Font = new Font("Segoe UI", 8.5F, FontStyle.Bold),
                ForeColor = Color.FromArgb(148, 163, 184),
                Location = new Point(20, 442),
                AutoSize = true
            };
            this.Controls.Add(lblDetailsHeader);

            txtDetails = new TextBox
            {
                Location = new Point(20, 462),
                Size = new Size(910, 130),
                Multiline = true,
                ReadOnly = true,
                BackColor = Color.FromArgb(30, 41, 59),
                ForeColor = Color.FromArgb(226, 232, 240),
                BorderStyle = BorderStyle.FixedSingle,
                ScrollBars = ScrollBars.Vertical,
                Font = new Font("Segoe UI", 9F)
            };
            this.Controls.Add(txtDetails);

            // Action Buttons
            btnApprove = new Button
            {
                Text = "✓ Approve Loan Application",
                Location = new Point(20, 608),
                Size = new Size(260, 46),
                BackColor = Color.FromArgb(16, 185, 129),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 10F, FontStyle.Bold),
                Cursor = Cursors.Hand,
                Enabled = false
            };
            btnApprove.FlatAppearance.BorderSize = 0;
            btnApprove.Click += async (s, e) => await ExecuteReviewAsync(isApprove: true);
            this.Controls.Add(btnApprove);

            btnReject = new Button
            {
                Text = "✗ Reject Application",
                Location = new Point(290, 608),
                Size = new Size(220, 46),
                BackColor = Color.FromArgb(244, 63, 94),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 10F, FontStyle.Bold),
                Cursor = Cursors.Hand,
                Enabled = false
            };
            btnReject.FlatAppearance.BorderSize = 0;
            btnReject.Click += async (s, e) => await ExecuteReviewAsync(isApprove: false);
            this.Controls.Add(btnReject);

            var btnClose = new Button
            {
                Text = "Close",
                Location = new Point(810, 608),
                Size = new Size(120, 46),
                BackColor = Color.FromArgb(51, 65, 85),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 10F, FontStyle.Bold),
                Cursor = Cursors.Hand
            };
            btnClose.FlatAppearance.BorderSize = 0;
            btnClose.Click += (s, e) => this.Close();
            this.Controls.Add(btnClose);
        }

        private async Task LoadLoansAsync()
        {
            try
            {
                var filter = cmbStatusFilter.SelectedItem?.ToString();
                var endpoint = "admin/loans";
                if (filter != null && filter != "All Statuses")
                {
                    endpoint += $"?status={filter}";
                }

                var response = await ApiClient.GetAsync<ApiResponse<List<LoanApplicationDto>>>(endpoint);
                _applications = response?.Data ?? new List<LoanApplicationDto>();

                var statsRes = await ApiClient.GetAsync<ApiResponse<AdminLoanStatsDto>>("admin/loans/stats");
                if (statsRes?.Data != null)
                {
                    lblStats.Text = $"Total: {statsRes.Data.TotalApplications} | Pending: {statsRes.Data.PendingCount} | Approved: {statsRes.Data.ApprovedCount} | Rejected: {statsRes.Data.RejectedCount}";
                }

                dgvLoans.Rows.Clear();
                foreach (var app in _applications)
                {
                    dgvLoans.Rows.Add(
                        app.ApplicationNumber,
                        app.CustomerName,
                        app.AccountNumber,
                        app.LoanType,
                        $"৳{app.RequestedAmount:N2}",
                        $"{app.EligibilityScore} ({app.EligibilityCategory})",
                        app.Status,
                        app.CreatedAt.ToString("MMM dd, yyyy HH:mm")
                    );
                }

                if (dgvLoans.Rows.Count > 0)
                {
                    dgvLoans.Rows[0].Selected = true;
                }
                else
                {
                    txtDetails.Text = "No loan applications match criteria.";
                    btnApprove.Enabled = false;
                    btnReject.Enabled = false;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to load loans: {ex.Message}", "Admin Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void DgvLoans_SelectionChanged(object? sender, EventArgs e)
        {
            if (dgvLoans.SelectedRows.Count == 0 || dgvLoans.SelectedRows[0].Index >= _applications.Count)
            {
                txtDetails.Text = "";
                btnApprove.Enabled = false;
                btnReject.Enabled = false;
                return;
            }

            var app = _applications[dgvLoans.SelectedRows[0].Index];
            var reviewInfo = app.ReviewedAt.HasValue
                ? $"\r\nReview Decision: {app.Status} by {app.ReviewedBy} on {app.ReviewedAt:MMM dd, yyyy HH:mm} UTC\r\nAdmin Note: \"{app.AdminNote}\""
                : "\r\nUnderwriting Decision: Pending admin action.";

            var incomeInfo = app.MonthlyIncome.HasValue ? $" | Declared Income: ৳{app.MonthlyIncome.Value:N2}" : "";

            txtDetails.Text =
                $"Applicant: {app.CustomerName} ({app.CustomerEmail}, Phone: {app.CustomerPhone})\r\n" +
                $"Account: {app.AccountNumber} | Loan Type: {app.LoanType}{incomeInfo}\r\n" +
                $"Requested Amount: ৳{app.RequestedAmount:N2} | Eligible Cap: ৳{app.EligibleAmount:N2} | Score: {app.EligibilityScore}/100 ({app.EligibilityCategory})\r\n" +
                $"Purpose: {app.Purpose}\r\n" +
                $"Status: {app.Status}" + reviewInfo;

            btnApprove.Enabled = app.Status == "Pending";
            btnReject.Enabled = app.Status == "Pending";
        }

        private async Task ExecuteReviewAsync(bool isApprove)
        {
            if (dgvLoans.SelectedRows.Count == 0 || dgvLoans.SelectedRows[0].Index >= _applications.Count) return;

            var app = _applications[dgvLoans.SelectedRows[0].Index];
            var actionText = isApprove ? "APPROVE" : "REJECT";

            var confirm = MessageBox.Show(
                $"Are you sure you want to {actionText} Loan Application {app.ApplicationNumber} for {app.CustomerName} (৳{app.RequestedAmount:N2})?",
                $"Confirm Loan {actionText}",
                MessageBoxButtons.YesNo,
                isApprove ? MessageBoxIcon.Question : MessageBoxIcon.Warning);

            if (confirm != DialogResult.Yes) return;

            var defaultComment = isApprove ? "Eligibility requirements satisfied." : "Eligibility requirements not satisfied.";
            var endpoint = $"admin/loans/{app.ApplicationNumber}/{(isApprove ? "approve" : "reject")}";

            try
            {
                var req = new AdminLoanReviewRequest { Comment = defaultComment };
                var response = await ApiClient.PostAsync<ApiResponse<LoanApplicationDto>>(endpoint, req);

                if (response != null && response.Success)
                {
                    MessageBox.Show($"Loan application {app.ApplicationNumber} has been {(isApprove ? "approved" : "rejected")} successfully.", "Review Success", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    await LoadLoansAsync();
                }
                else
                {
                    MessageBox.Show(response?.Message ?? "Action failed.", "Review Error", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error executing review: {ex.Message}", "Review Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }
}
