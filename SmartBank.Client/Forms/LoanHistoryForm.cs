using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Forms;
using SmartBank.Client.Models.Common;
using SmartBank.Client.Models.Loans;
using SmartBank.Client.Services;

namespace SmartBank.Client.Forms
{
    public class LoanHistoryForm : Form
    {
        private DataGridView dgvLoans = null!;
        private TextBox txtDetails = null!;
        private Button btnRefresh = null!;
        private List<LoanApplicationDto> _applications = new();

        public LoanHistoryForm()
        {
            InitializeComponents();
            _ = LoadLoanHistoryAsync();
        }

        private void InitializeComponents()
        {
            this.Text = "SmartBank — Customer Loan Applications History";
            this.ClientSize = new Size(820, 580);
            this.StartPosition = FormStartPosition.CenterParent;
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.BackColor = Color.FromArgb(15, 23, 42); // Slate 900
            this.ForeColor = Color.White;
            this.Font = new Font("Segoe UI", 9F, FontStyle.Regular, GraphicsUnit.Point);

            var pnlHeader = new Panel
            {
                Dock = DockStyle.Top,
                Height = 65,
                BackColor = Color.FromArgb(30, 41, 59),
                Padding = new Padding(20, 12, 20, 12)
            };

            var lblTitle = new Label
            {
                Text = "My Loan Applications History",
                Font = new Font("Segoe UI", 13F, FontStyle.Bold),
                ForeColor = Color.White,
                Location = new Point(20, 16),
                AutoSize = true
            };

            btnRefresh = new Button
            {
                Text = "Refresh",
                Location = new Point(690, 16),
                Size = new Size(100, 32),
                BackColor = Color.FromArgb(51, 65, 85),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat
            };
            btnRefresh.Click += async (s, e) => await LoadLoanHistoryAsync();

            pnlHeader.Controls.Add(lblTitle);
            pnlHeader.Controls.Add(btnRefresh);
            this.Controls.Add(pnlHeader);

            // DataGridView
            dgvLoans = new DataGridView
            {
                Location = new Point(20, 80),
                Size = new Size(780, 320),
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
            dgvLoans.Columns.Add("LoanType", "Type");
            dgvLoans.Columns.Add("Amount", "Amount (৳)");
            dgvLoans.Columns.Add("Score", "Score");
            dgvLoans.Columns.Add("Status", "Status");
            dgvLoans.Columns.Add("Date", "Submission Date");

            dgvLoans.SelectionChanged += DgvLoans_SelectionChanged;
            this.Controls.Add(dgvLoans);

            // Details panel
            var lblDetailsHeader = new Label
            {
                Text = "APPLICATION DETAILS & UNDERWRITING DECISION",
                Font = new Font("Segoe UI", 8.5F, FontStyle.Bold),
                ForeColor = Color.FromArgb(148, 163, 184),
                Location = new Point(20, 415),
                AutoSize = true
            };
            this.Controls.Add(lblDetailsHeader);

            txtDetails = new TextBox
            {
                Location = new Point(20, 435),
                Size = new Size(780, 120),
                Multiline = true,
                ReadOnly = true,
                BackColor = Color.FromArgb(30, 41, 59),
                ForeColor = Color.FromArgb(226, 232, 240),
                BorderStyle = BorderStyle.FixedSingle,
                ScrollBars = ScrollBars.Vertical,
                Font = new Font("Segoe UI", 9F)
            };
            this.Controls.Add(txtDetails);
        }

        private async Task LoadLoanHistoryAsync()
        {
            try
            {
                var response = await ApiClient.GetAsync<ApiResponse<List<LoanApplicationDto>>>("loans/my-applications");
                _applications = response?.Data ?? new List<LoanApplicationDto>();

                dgvLoans.Rows.Clear();
                foreach (var app in _applications)
                {
                    dgvLoans.Rows.Add(
                        app.ApplicationNumber,
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
                    txtDetails.Text = "No loan applications found on file.";
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to load loan applications: {ex.Message}", "History Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void DgvLoans_SelectionChanged(object? sender, EventArgs e)
        {
            if (dgvLoans.SelectedRows.Count == 0 || dgvLoans.SelectedRows[0].Index >= _applications.Count)
            {
                txtDetails.Text = "";
                return;
            }

            var app = _applications[dgvLoans.SelectedRows[0].Index];
            var reviewInfo = app.ReviewedAt.HasValue
                ? $"\r\nReviewed By: {app.ReviewedBy} on {app.ReviewedAt:MMM dd, yyyy HH:mm} UTC\r\nAdmin Note: \"{app.AdminNote}\""
                : "\r\nUnderwriting Status: Awaiting bank credit officer review.";

            txtDetails.Text =
                $"Application: {app.ApplicationNumber} ({app.LoanType} Loan)\r\n" +
                $"Requested: ৳{app.RequestedAmount:N2} | Eligible Cap: ৳{app.EligibleAmount:N2} | Score: {app.EligibilityScore}/100\r\n" +
                $"Purpose: {app.Purpose}\r\n" +
                $"Status: {app.Status}" + reviewInfo;
        }
    }
}
