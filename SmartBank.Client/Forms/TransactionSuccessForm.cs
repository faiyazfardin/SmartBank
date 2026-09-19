using System;
using System.Drawing;
using System.Windows.Forms;

namespace SmartBank.Client.Forms
{
    public class TransactionSuccessForm : Form
    {
        public TransactionSuccessForm(string transactionType, decimal amount, string trackingId, decimal newBalance)
        {
            this.Text = "Transaction Successful — SmartBank";
            this.ClientSize = new Size(420, 360);
            this.StartPosition = FormStartPosition.CenterParent;
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.BackColor = Color.White;
            this.Font = new Font("Segoe UI", 9F);

            var pnlHeader = new Panel
            {
                Dock = DockStyle.Top,
                Height = 80,
                BackColor = Color.FromArgb(240, 253, 244)
            };

            var lblTitle = new Label
            {
                Text = "✔ Transaction Successful!",
                Font = new Font("Segoe UI", 12F, FontStyle.Bold),
                ForeColor = Color.FromArgb(22, 101, 52),
                Location = new Point(20, 16),
                AutoSize = true
            };

            var lblSub = new Label
            {
                Text = $"Your {transactionType} was processed securely.",
                Font = new Font("Segoe UI", 8.5F),
                ForeColor = Color.FromArgb(22, 101, 52),
                Location = new Point(20, 44),
                AutoSize = true
            };

            pnlHeader.Controls.AddRange(new Control[] { lblTitle, lblSub });

            var pnlDetails = new Panel
            {
                Location = new Point(20, 100),
                Size = new Size(380, 180),
                BackColor = Color.FromArgb(248, 250, 252)
            };

            var lblTrackingHeader = new Label
            {
                Text = "Tracking ID:",
                Font = new Font("Segoe UI", 8.5F),
                ForeColor = Color.FromArgb(100, 116, 139),
                Location = new Point(16, 16),
                AutoSize = true
            };

            var lblTrackingVal = new Label
            {
                Text = trackingId,
                Font = new Font("Segoe UI", 10F, FontStyle.Bold),
                ForeColor = Color.FromArgb(15, 23, 42),
                Location = new Point(140, 15),
                AutoSize = true
            };

            var lblTypeHeader = new Label
            {
                Text = "Type:",
                Font = new Font("Segoe UI", 8.5F),
                ForeColor = Color.FromArgb(100, 116, 139),
                Location = new Point(16, 50),
                AutoSize = true
            };

            var lblTypeVal = new Label
            {
                Text = transactionType,
                Font = new Font("Segoe UI", 9.5F, FontStyle.Bold),
                ForeColor = Color.FromArgb(37, 99, 235),
                Location = new Point(140, 49),
                AutoSize = true
            };

            var lblAmtHeader = new Label
            {
                Text = "Amount:",
                Font = new Font("Segoe UI", 8.5F),
                ForeColor = Color.FromArgb(100, 116, 139),
                Location = new Point(16, 84),
                AutoSize = true
            };

            var lblAmtVal = new Label
            {
                Text = $"৳{amount:N2}",
                Font = new Font("Segoe UI", 10.5F, FontStyle.Bold),
                ForeColor = Color.FromArgb(22, 101, 52),
                Location = new Point(140, 82),
                AutoSize = true
            };

            var lblBalHeader = new Label
            {
                Text = "Updated Balance:",
                Font = new Font("Segoe UI", 8.5F),
                ForeColor = Color.FromArgb(100, 116, 139),
                Location = new Point(16, 118),
                AutoSize = true
            };

            var lblBalVal = new Label
            {
                Text = $"৳{newBalance:N2}",
                Font = new Font("Segoe UI", 10.5F, FontStyle.Bold),
                ForeColor = Color.FromArgb(37, 99, 235),
                Location = new Point(140, 116),
                AutoSize = true
            };

            pnlDetails.Controls.AddRange(new Control[]
            {
                lblTrackingHeader, lblTrackingVal,
                lblTypeHeader, lblTypeVal,
                lblAmtHeader, lblAmtVal,
                lblBalHeader, lblBalVal
            });

            var btnClose = new Button
            {
                Text = "Close",
                DialogResult = DialogResult.OK,
                Location = new Point(20, 300),
                Size = new Size(380, 40),
                BackColor = Color.FromArgb(37, 99, 235),
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 9.5F, FontStyle.Bold),
                FlatStyle = FlatStyle.Flat,
                Cursor = Cursors.Hand
            };
            btnClose.FlatAppearance.BorderSize = 0;

            this.Controls.AddRange(new Control[] { pnlHeader, pnlDetails, btnClose });
        }
    }
}
