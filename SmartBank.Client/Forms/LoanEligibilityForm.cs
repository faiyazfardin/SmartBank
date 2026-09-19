using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Threading.Tasks;
using System.Windows.Forms;
using SmartBank.Client.Models.Common;
using SmartBank.Client.Models.Loans;
using SmartBank.Client.Services;

namespace SmartBank.Client.Forms
{
    public class LoanEligibilityForm : Form
    {
        private Label lblScore = null!;
        private Label lblCategory = null!;
        private Label lblStatus = null!;
        private Label lblMaxAmount = null!;
        private Label lblAvgBalance = null!;
        private Label lblAccountAge = null!;
        private ListBox lstReasons = null!;
        private Button btnApply = null!;
        private Button btnViewHistory = null!;
        private ProgressBar prgScore = null!;
        private LoanEligibilityDto? _eligibilityData;

        public LoanEligibilityForm()
        {
            InitializeComponents();
            _ = LoadEligibilityAsync();
        }

        private void InitializeComponents()
        {
            this.Text = "SmartBank — Loan Eligibility Assessment";
            this.ClientSize = new Size(680, 720);
            this.StartPosition = FormStartPosition.CenterParent;
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.BackColor = Color.FromArgb(15, 23, 42); // Slate 900
            this.ForeColor = Color.White;
            this.Font = new Font("Segoe UI", 9.5F, FontStyle.Regular, GraphicsUnit.Point);

            // Header Panel
            var pnlHeader = new Panel
            {
                Dock = DockStyle.Top,
                Height = 80,
                BackColor = Color.FromArgb(30, 41, 59),
                Padding = new Padding(24, 16, 24, 16)
            };

            var lblTitle = new Label
            {
                Text = "💰 Smart Loan Eligibility",
                Font = new Font("Segoe UI", 16F, FontStyle.Bold),
                ForeColor = Color.White,
                Location = new Point(24, 16),
                AutoSize = true
            };

            var lblSub = new Label
            {
                Text = "Automated rule-based underwriting and maximum credit limit evaluation",
                Font = new Font("Segoe UI", 9F),
                ForeColor = Color.FromArgb(148, 163, 184),
                Location = new Point(26, 48),
                AutoSize = true
            };

            pnlHeader.Controls.Add(lblTitle);
            pnlHeader.Controls.Add(lblSub);
            this.Controls.Add(pnlHeader);

            // Main Score & Status Card
            var pnlScoreCard = new Panel
            {
                Location = new Point(24, 96),
                Size = new Size(632, 190),
                BackColor = Color.FromArgb(30, 41, 59)
            };
            pnlScoreCard.Paint += (s, e) =>
            {
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                using var pen = new Pen(Color.FromArgb(51, 65, 85), 1);
                e.Graphics.DrawRectangle(pen, 0, 0, pnlScoreCard.Width - 1, pnlScoreCard.Height - 1);
            };

            var lblScoreTitle = new Label
            {
                Text = "ELIGIBILITY SCORE",
                Font = new Font("Segoe UI", 8F, FontStyle.Bold),
                ForeColor = Color.FromArgb(148, 163, 184),
                Location = new Point(20, 20),
                AutoSize = true
            };

            lblScore = new Label
            {
                Text = "-- / 100",
                Font = new Font("Segoe UI", 32F, FontStyle.Bold),
                ForeColor = Color.FromArgb(52, 211, 153),
                Location = new Point(16, 38),
                AutoSize = true
            };

            lblCategory = new Label
            {
                Text = "Category: Evaluating...",
                Font = new Font("Segoe UI", 10F, FontStyle.Bold),
                ForeColor = Color.White,
                Location = new Point(22, 106),
                AutoSize = true
            };

            prgScore = new ProgressBar
            {
                Location = new Point(22, 136),
                Size = new Size(300, 10),
                Style = ProgressBarStyle.Continuous,
                Value = 0
            };

            lblStatus = new Label
            {
                Text = "STATUS: CHECKING",
                Font = new Font("Segoe UI", 11F, FontStyle.Bold),
                ForeColor = Color.FromArgb(251, 191, 36),
                Location = new Point(360, 24),
                AutoSize = true
            };

            var lblMaxTitle = new Label
            {
                Text = "MAX ELIGIBLE AMOUNT",
                Font = new Font("Segoe UI", 8F, FontStyle.Bold),
                ForeColor = Color.FromArgb(148, 163, 184),
                Location = new Point(360, 60),
                AutoSize = true
            };

            lblMaxAmount = new Label
            {
                Text = "৳0.00",
                Font = new Font("Segoe UI", 24F, FontStyle.Bold),
                ForeColor = Color.FromArgb(56, 189, 248),
                Location = new Point(356, 78),
                AutoSize = true
            };

            lblAvgBalance = new Label
            {
                Text = "Avg Balance: ৳0.00",
                Font = new Font("Segoe UI", 8.5F),
                ForeColor = Color.FromArgb(148, 163, 184),
                Location = new Point(360, 132),
                AutoSize = true
            };

            lblAccountAge = new Label
            {
                Text = "Account Maturity: 0 months",
                Font = new Font("Segoe UI", 8.5F),
                ForeColor = Color.FromArgb(148, 163, 184),
                Location = new Point(360, 152),
                AutoSize = true
            };

            pnlScoreCard.Controls.AddRange(new Control[]
            {
                lblScoreTitle, lblScore, lblCategory, prgScore,
                lblStatus, lblMaxTitle, lblMaxAmount, lblAvgBalance, lblAccountAge
            });
            this.Controls.Add(pnlScoreCard);

            // Reasons Section
            var lblReasonsHeader = new Label
            {
                Text = "DECISION FACTORS & REASONS",
                Font = new Font("Segoe UI", 9F, FontStyle.Bold),
                ForeColor = Color.FromArgb(148, 163, 184),
                Location = new Point(24, 300),
                AutoSize = true
            };
            this.Controls.Add(lblReasonsHeader);

            lstReasons = new ListBox
            {
                Location = new Point(24, 324),
                Size = new Size(632, 280),
                BackColor = Color.FromArgb(30, 41, 59),
                ForeColor = Color.FromArgb(226, 232, 240),
                BorderStyle = BorderStyle.FixedSingle,
                Font = new Font("Segoe UI", 9.5F),
                ItemHeight = 28
            };
            this.Controls.Add(lstReasons);

            // Action Buttons
            btnApply = new Button
            {
                Text = "Apply for Loan Now",
                Location = new Point(24, 624),
                Size = new Size(330, 48),
                BackColor = Color.FromArgb(16, 185, 129),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 10.5F, FontStyle.Bold),
                Cursor = Cursors.Hand,
                Enabled = false
            };
            btnApply.FlatAppearance.BorderSize = 0;
            btnApply.Click += BtnApply_Click;
            this.Controls.Add(btnApply);

            btnViewHistory = new Button
            {
                Text = "My Applications",
                Location = new Point(368, 624),
                Size = new Size(288, 48),
                BackColor = Color.FromArgb(51, 65, 85),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 10F, FontStyle.Bold),
                Cursor = Cursors.Hand
            };
            btnViewHistory.FlatAppearance.BorderSize = 0;
            btnViewHistory.Click += (s, e) =>
            {
                using var histForm = new LoanHistoryForm();
                histForm.ShowDialog(this);
            };
            this.Controls.Add(btnViewHistory);
        }

        private async Task LoadEligibilityAsync()
        {
            try
            {
                var response = await ApiClient.GetAsync<ApiResponse<LoanEligibilityDto>>("loans/eligibility");
                if (response?.Data != null)
                {
                    _eligibilityData = response.Data;
                    lblScore.Text = $"{_eligibilityData.Score} / 100";
                    lblCategory.Text = $"Category: {_eligibilityData.Category}";
                    prgScore.Value = Math.Clamp(_eligibilityData.Score, 0, 100);

                    if (_eligibilityData.Eligible)
                    {
                        lblStatus.Text = "STATUS: ✓ ELIGIBLE";
                        lblStatus.ForeColor = Color.FromArgb(52, 211, 153);
                        lblMaxAmount.Text = $"৳{_eligibilityData.MaximumAmount:N0}";
                        btnApply.Enabled = true;
                    }
                    else
                    {
                        lblStatus.Text = "STATUS: ✗ NOT ELIGIBLE";
                        lblStatus.ForeColor = Color.FromArgb(244, 63, 94);
                        lblMaxAmount.Text = "৳0.00";
                        btnApply.Enabled = false;
                    }

                    lblAvgBalance.Text = $"Avg Monthly Balance: ৳{_eligibilityData.AverageMonthlyBalance:N2}";
                    lblAccountAge.Text = $"Account Age: {_eligibilityData.AccountAgeMonths:F1} months ({_eligibilityData.TotalTransactions} txs)";

                    lstReasons.Items.Clear();
                    foreach (var r in _eligibilityData.Reasons)
                    {
                        lstReasons.Items.Add(r);
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to evaluate loan eligibility: {ex.Message}", "Eligibility Check", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void BtnApply_Click(object? sender, EventArgs e)
        {
            if (_eligibilityData == null || !_eligibilityData.Eligible) return;

            using var applyForm = new LoanApplicationForm(_eligibilityData.MaximumAmount);
            if (applyForm.ShowDialog(this) == DialogResult.OK)
            {
                _ = LoadEligibilityAsync();
            }
        }
    }
}
