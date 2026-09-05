using System;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Forms;
using SmartBank.Client.Models.Common;
using SmartBank.Client.Models.Loans;
using SmartBank.Client.Services;

namespace SmartBank.Client.Forms
{
    public class LoanApplicationForm : Form
    {
        private readonly decimal _maxAmount;
        private ComboBox cmbLoanType = null!;
        private NumericUpDown numAmount = null!;
        private NumericUpDown numIncome = null!;
        private TextBox txtPurpose = null!;
        private Label lblAmountError = null!;
        private Label lblPurposeError = null!;
        private Button btnSubmit = null!;

        public LoanApplicationForm(decimal maxAmount)
        {
            _maxAmount = maxAmount;
            InitializeComponents();
        }

        private void InitializeComponents()
        {
            this.Text = "SmartBank — Submit Loan Application";
            this.ClientSize = new Size(520, 580);
            this.StartPosition = FormStartPosition.CenterParent;
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.BackColor = Color.FromArgb(15, 23, 42); // Slate 900
            this.ForeColor = Color.White;
            this.Font = new Font("Segoe UI", 9.5F, FontStyle.Regular, GraphicsUnit.Point);

            var pnlHeader = new Panel
            {
                Dock = DockStyle.Top,
                Height = 70,
                BackColor = Color.FromArgb(30, 41, 59),
                Padding = new Padding(20, 14, 20, 14)
            };

            var lblTitle = new Label
            {
                Text = "Apply for Loan Financing",
                Font = new Font("Segoe UI", 14F, FontStyle.Bold),
                ForeColor = Color.White,
                Location = new Point(20, 12),
                AutoSize = true
            };

            var lblSub = new Label
            {
                Text = $"Pre-Approved Max Limit: ৳{_maxAmount:N0}",
                Font = new Font("Segoe UI", 8.5F),
                ForeColor = Color.FromArgb(52, 211, 153),
                Location = new Point(22, 40),
                AutoSize = true
            };

            pnlHeader.Controls.Add(lblTitle);
            pnlHeader.Controls.Add(lblSub);
            this.Controls.Add(pnlHeader);

            int startY = 90;

            // 1. Loan Type
            var lblType = new Label
            {
                Text = "Loan Type *",
                Font = new Font("Segoe UI", 9F, FontStyle.Bold),
                ForeColor = Color.FromArgb(148, 163, 184),
                Location = new Point(24, startY),
                AutoSize = true
            };
            this.Controls.Add(lblType);

            cmbLoanType = new ComboBox
            {
                Location = new Point(24, startY + 22),
                Size = new Size(470, 32),
                DropDownStyle = ComboBoxStyle.DropDownList,
                BackColor = Color.FromArgb(30, 41, 59),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat
            };
            cmbLoanType.Items.AddRange(new object[] { "Personal", "Business", "Education", "Emergency", "Home" });
            cmbLoanType.SelectedIndex = 0;
            this.Controls.Add(cmbLoanType);

            // 2. Monthly Income
            var lblIncome = new Label
            {
                Text = "Declared Monthly Income (৳)",
                Font = new Font("Segoe UI", 9F, FontStyle.Bold),
                ForeColor = Color.FromArgb(148, 163, 184),
                Location = new Point(24, startY + 70),
                AutoSize = true
            };
            this.Controls.Add(lblIncome);

            numIncome = new NumericUpDown
            {
                Location = new Point(24, startY + 92),
                Size = new Size(470, 32),
                Minimum = 0,
                Maximum = 10000000,
                Increment = 5000,
                Value = 50000,
                ThousandsSeparator = true,
                BackColor = Color.FromArgb(30, 41, 59),
                ForeColor = Color.White
            };
            this.Controls.Add(numIncome);

            // 3. Requested Amount
            var lblAmt = new Label
            {
                Text = $"Requested Amount (৳) * [Max: ৳{_maxAmount:N0}]",
                Font = new Font("Segoe UI", 9F, FontStyle.Bold),
                ForeColor = Color.FromArgb(148, 163, 184),
                Location = new Point(24, startY + 140),
                AutoSize = true
            };
            this.Controls.Add(lblAmt);

            numAmount = new NumericUpDown
            {
                Location = new Point(24, startY + 162),
                Size = new Size(470, 32),
                Minimum = 1000,
                Maximum = Math.Max(1000, _maxAmount),
                Increment = 5000,
                Value = Math.Min(50000, Math.Max(1000, _maxAmount)),
                ThousandsSeparator = true,
                BackColor = Color.FromArgb(30, 41, 59),
                ForeColor = Color.FromArgb(52, 211, 153),
                Font = new Font("Segoe UI", 11F, FontStyle.Bold)
            };
            numAmount.ValueChanged += (s, e) => ValidateForm();
            this.Controls.Add(numAmount);

            lblAmountError = new Label
            {
                Text = "",
                ForeColor = Color.FromArgb(244, 63, 94),
                Font = new Font("Segoe UI", 8F),
                Location = new Point(24, startY + 196),
                AutoSize = true
            };
            this.Controls.Add(lblAmountError);

            // 4. Purpose
            var lblPurp = new Label
            {
                Text = "Loan Purpose & Justification *",
                Font = new Font("Segoe UI", 9F, FontStyle.Bold),
                ForeColor = Color.FromArgb(148, 163, 184),
                Location = new Point(24, startY + 220),
                AutoSize = true
            };
            this.Controls.Add(lblPurp);

            txtPurpose = new TextBox
            {
                Location = new Point(24, startY + 242),
                Size = new Size(470, 90),
                Multiline = true,
                BackColor = Color.FromArgb(30, 41, 59),
                ForeColor = Color.White,
                BorderStyle = BorderStyle.FixedSingle,
                ScrollBars = ScrollBars.Vertical
            };
            txtPurpose.TextChanged += (s, e) => ValidateForm();
            this.Controls.Add(txtPurpose);

            lblPurposeError = new Label
            {
                Text = "",
                ForeColor = Color.FromArgb(244, 63, 94),
                Font = new Font("Segoe UI", 8F),
                Location = new Point(24, startY + 336),
                AutoSize = true
            };
            this.Controls.Add(lblPurposeError);

            // Submit Button
            btnSubmit = new Button
            {
                Text = "Submit Application",
                Location = new Point(24, startY + 370),
                Size = new Size(470, 48),
                BackColor = Color.FromArgb(99, 102, 241),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 10.5F, FontStyle.Bold),
                Cursor = Cursors.Hand
            };
            btnSubmit.FlatAppearance.BorderSize = 0;
            btnSubmit.Click += async (s, e) => await SubmitLoanAsync();
            this.Controls.Add(btnSubmit);
        }

        private bool ValidateForm()
        {
            bool isValid = true;

            if (numAmount.Value > _maxAmount)
            {
                lblAmountError.Text = $"Amount exceeds your maximum pre-approved cap of ৳{_maxAmount:N0}.";
                isValid = false;
            }
            else if (numAmount.Value < 1000)
            {
                lblAmountError.Text = "Minimum loan amount is ৳1,000.";
                isValid = false;
            }
            else
            {
                lblAmountError.Text = "";
            }

            if (string.IsNullOrWhiteSpace(txtPurpose.Text) || txtPurpose.Text.Trim().Length < 5)
            {
                lblPurposeError.Text = "Please provide at least 5 characters explaining loan purpose.";
                isValid = false;
            }
            else
            {
                lblPurposeError.Text = "";
            }

            btnSubmit.Enabled = isValid;
            return isValid;
        }

        private async Task SubmitLoanAsync()
        {
            if (!ValidateForm()) return;

            btnSubmit.Enabled = false;
            btnSubmit.Text = "Submitting...";

            try
            {
                var req = new ApplyLoanRequest
                {
                    LoanType = cmbLoanType.SelectedItem?.ToString() ?? "Personal",
                    RequestedAmount = numAmount.Value,
                    Purpose = txtPurpose.Text.Trim(),
                    MonthlyIncome = numIncome.Value > 0 ? numIncome.Value : null
                };

                var response = await ApiClient.PostAsync<ApiResponse<LoanApplicationDto>>("loans/apply", req);

                if (response != null && response.Success && response.Data != null)
                {
                    MessageBox.Show(
                        $"Loan application {response.Data.ApplicationNumber} has been successfully submitted!\n\nStatus: PENDING REVIEW\nOur underwriting team will evaluate your request.",
                        "Application Submitted",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Information);

                    this.DialogResult = DialogResult.OK;
                    this.Close();
                }
                else
                {
                    var msg = response?.Message ?? "Failed to submit loan application.";
                    MessageBox.Show(msg, "Submission Error", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error submitting loan application: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                btnSubmit.Enabled = true;
                btnSubmit.Text = "Submit Application";
            }
        }
    }
}
