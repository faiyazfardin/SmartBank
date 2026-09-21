using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using SmartBank.Data;
using SmartBank.DTOs.Common;
using SmartBank.DTOs.Loans;
using SmartBank.Entities;
using SmartBank.Services.Interfaces;

namespace SmartBank.Services
{
    public class LoanService : ILoanService
    {
        private readonly SmartBankDbContext _context;
        private readonly ILoanEligibilityService _eligibilityService;
        private readonly ILoanCalculatorService _calculatorService;

        public LoanService(
            SmartBankDbContext context, 
            ILoanEligibilityService eligibilityService,
            ILoanCalculatorService calculatorService)
        {
            _context = context;
            _eligibilityService = eligibilityService;
            _calculatorService = calculatorService;
        }

        public async Task<(int StatusCode, ApiResponse<LoanApplicationDto> Response)> ApplyForLoanAsync(int userId, ApplyLoanRequest request)
        {
            var user = await _context.Users
                .Include(u => u.Accounts)
                .FirstOrDefaultAsync(u => u.Id == userId);

            if (user == null)
            {
                return (404, ApiResponse<LoanApplicationDto>.FailureResponse("User record not found."));
            }

            var account = user.Accounts.FirstOrDefault();
            if (account == null)
            {
                return (400, ApiResponse<LoanApplicationDto>.FailureResponse("No active banking account found for this user."));
            }

            if (!account.IsActive || user.Status.Equals("Suspended", StringComparison.OrdinalIgnoreCase))
            {
                return (403, ApiResponse<LoanApplicationDto>.FailureResponse("Your account must be active to apply for a loan."));
            }

            // Check for existing pending application
            var hasPending = await _context.LoanApplications
                .AnyAsync(l => l.UserId == userId && l.Status == "Pending");

            if (hasPending)
            {
                return (400, ApiResponse<LoanApplicationDto>.FailureResponse("You already have a pending loan application. Please wait for bank review."));
            }

            // Server-side eligibility re-evaluation
            var eligibility = await _eligibilityService.EvaluateEligibilityAsync(userId);

            if (!eligibility.Eligible)
            {
                var reasonsStr = string.Join("; ", eligibility.Reasons.Where(r => r.StartsWith("✗")));
                return (400, ApiResponse<LoanApplicationDto>.FailureResponse(
                    "You are not currently eligible for a loan.",
                    string.IsNullOrWhiteSpace(reasonsStr) ? "Eligibility score did not meet the required threshold." : reasonsStr));
            }

            if (request.RequestedAmount <= 0)
            {
                return (400, ApiResponse<LoanApplicationDto>.FailureResponse("Requested amount must be greater than zero."));
            }

            if (request.RequestedAmount > eligibility.MaximumAmount)
            {
                return (400, ApiResponse<LoanApplicationDto>.FailureResponse(
                    $"Requested amount exceeds your maximum eligible amount of ৳{eligibility.MaximumAmount:N2}."));
            }

            // Validate requested tenure against loan type limits
            var loanType = request.LoanType ?? "Personal";
            var (minTenure, maxTenure) = LoanTenureLimits.GetAllowedRange(loanType);
            var tenure = request.RequestedTenureMonths;
            if (tenure < minTenure || tenure > maxTenure)
            {
                return (400, ApiResponse<LoanApplicationDto>.FailureResponse(
                    $"For {loanType} Loan, tenure must be between {minTenure} and {maxTenure} months."));
            }

            // Generate sequential application number
            var currentCount = await _context.LoanApplications.CountAsync();
            var appNumber = $"LN-{(currentCount + 1):D5}";

            while (await _context.LoanApplications.AnyAsync(l => l.ApplicationNumber == appNumber))
            {
                currentCount++;
                appNumber = $"LN-{(currentCount + 1):D5}";
            }

            var indicativeRate = eligibility.IndicativeRate;

            var loanApp = new LoanApplication
            {
                ApplicationNumber = appNumber,
                UserId = user.Id,
                AccountId = account.Id,
                LoanType = loanType,
                RequestedAmount = request.RequestedAmount,
                EligibleAmount = eligibility.MaximumAmount,
                EligibilityScore = eligibility.Score,
                EligibilityCategory = eligibility.Category,
                Purpose = request.Purpose?.Trim() ?? string.Empty,
                MonthlyIncome = request.MonthlyIncome,
                IndicativeRate = indicativeRate,
                RequestedTenureMonths = tenure,
                Status = "Pending",
                CreatedAt = DateTime.UtcNow
            };

            _context.LoanApplications.Add(loanApp);
            await _context.SaveChangesAsync();

            // Add Audit Log
            var auditLog = new LoanAuditLog
            {
                LoanApplicationId = loanApp.Id,
                Action = "ApplicationSubmitted",
                FieldChanged = "Status",
                OldValue = null,
                NewValue = "Pending",
                PerformedBy = user.Username,
                PerformedAt = DateTime.UtcNow,
                Note = $"Customer submitted {loanType} loan request for ৳{request.RequestedAmount:N2} ({tenure} months)."
            };
            _context.LoanAuditLogs.Add(auditLog);
            await _context.SaveChangesAsync();

            var dto = MapToDto(loanApp, user, account);
            return (201, ApiResponse<LoanApplicationDto>.SuccessResponse(dto, "Loan application submitted successfully."));
        }

        public async Task<List<LoanApplicationDto>> GetCustomerApplicationsAsync(int userId)
        {
            var apps = await _context.LoanApplications
                .Include(l => l.User)
                .Include(l => l.Account)
                .Where(l => l.UserId == userId)
                .OrderByDescending(l => l.CreatedAt)
                .ToListAsync();

            return apps.Select(a => MapToDto(a, a.User, a.Account)).ToList();
        }

        public async Task<LoanApplicationDto?> GetApplicationByNumberAsync(int userId, string applicationNumber, bool isAdmin = false)
        {
            var query = _context.LoanApplications
                .Include(l => l.User)
                .Include(l => l.Account)
                .Where(l => l.ApplicationNumber == applicationNumber);

            if (!isAdmin)
            {
                query = query.Where(l => l.UserId == userId);
            }

            var app = await query.FirstOrDefaultAsync();
            if (app == null) return null;

            return MapToDto(app, app.User, app.Account);
        }

        public async Task<List<LoanApplicationDto>> GetAllApplicationsForAdminAsync(string? statusFilter = null)
        {
            var query = _context.LoanApplications
                .Include(l => l.User)
                .Include(l => l.Account)
                .AsQueryable();

            if (!string.IsNullOrWhiteSpace(statusFilter) && !statusFilter.Equals("All", StringComparison.OrdinalIgnoreCase))
            {
                var norm = statusFilter.Trim();
                query = query.Where(l => l.Status == norm);
            }

            var apps = await query
                .OrderByDescending(l => l.CreatedAt)
                .ToListAsync();

            return apps.Select(a => MapToDto(a, a.User, a.Account)).ToList();
        }

        public async Task<(int StatusCode, ApiResponse<LoanApplicationDto> Response)> ReviewApplicationAsync(
            string applicationNumber, string adminUsername, bool isApprove, string comment)
        {
            var app = await _context.LoanApplications
                .Include(l => l.User)
                .Include(l => l.Account)
                .FirstOrDefaultAsync(l => l.ApplicationNumber == applicationNumber);

            if (app == null)
            {
                return (404, ApiResponse<LoanApplicationDto>.FailureResponse("Loan application not found."));
            }

            if (app.Status != "Pending")
            {
                return (400, ApiResponse<LoanApplicationDto>.FailureResponse($"This application has already been marked as '{app.Status}'."));
            }

            if (isApprove)
            {
                var approveDto = new ApproveLoanDto
                {
                    ApprovedAmount = app.RequestedAmount,
                    InterestRate = app.IndicativeRate ?? 10.50m,
                    TenureMonths = app.RequestedTenureMonths ?? 12,
                    AdminNote = comment
                };
                return await ApproveLoanAsync(app.Id, approveDto, adminUsername);
            }
            else
            {
                return await RejectLoanAsync(app.Id, comment, adminUsername);
            }
        }

        public async Task<(int StatusCode, ApiResponse<LoanApplicationDto> Response)> ApproveLoanAsync(
            int applicationId, ApproveLoanDto dto, string adminUsername)
        {
            var app = await _context.LoanApplications
                .Include(l => l.User)
                .Include(l => l.Account)
                .FirstOrDefaultAsync(l => l.Id == applicationId);

            if (app == null)
            {
                return (404, ApiResponse<LoanApplicationDto>.FailureResponse("Loan application not found."));
            }

            if (app.Status != "Pending")
            {
                return (400, ApiResponse<LoanApplicationDto>.FailureResponse($"Application is already in '{app.Status}' status."));
            }

            if (dto.ApprovedAmount <= 0)
            {
                return (400, ApiResponse<LoanApplicationDto>.FailureResponse("Approved amount must be greater than zero."));
            }

            var (minTenure, maxTenure) = LoanTenureLimits.GetAllowedRange(app.LoanType);
            if (dto.TenureMonths < minTenure || dto.TenureMonths > maxTenure)
            {
                return (400, ApiResponse<LoanApplicationDto>.FailureResponse(
                    $"Tenure for {app.LoanType} loan must be between {minTenure} and {maxTenure} months."));
            }

            if (dto.InterestRate <= 0 || dto.InterestRate > 50)
            {
                return (400, ApiResponse<LoanApplicationDto>.FailureResponse("Interest rate must be between 0.1% and 50%."));
            }

            // Compute Reducing Balance EMI and Schedule
            var emi = _calculatorService.CalculateEmi(dto.ApprovedAmount, dto.InterestRate, dto.TenureMonths);
            var totalRepayable = _calculatorService.CalculateTotalRepayable(emi, dto.TenureMonths);
            var firstDueDate = DateTime.UtcNow.AddMonths(1);
            var scheduleItems = _calculatorService.GenerateSchedule(dto.ApprovedAmount, dto.InterestRate, dto.TenureMonths, firstDueDate);

            // Check DTI
            string dtiWarning = string.Empty;
            if (app.MonthlyIncome.HasValue && app.MonthlyIncome.Value > 0)
            {
                var dti = _calculatorService.CalculateDti(emi, app.MonthlyIncome.Value);
                if (dti > 50m)
                {
                    dtiWarning = $" Note: High DTI ratio ({dti:F1}%).";
                }
            }

            using var transaction = await _context.Database.BeginTransactionAsync();
            try
            {
                // Update Application
                app.Status = "Approved";
                app.ApprovedAmount = dto.ApprovedAmount;
                app.FinalInterestRate = dto.InterestRate;
                app.FinalTenureMonths = dto.TenureMonths;
                app.FinalEmi = emi;
                app.TotalRepayable = totalRepayable;
                app.ReviewedAt = DateTime.UtcNow;
                app.ReviewedBy = adminUsername;
                app.AdminNote = (dto.AdminNote ?? string.Empty) + dtiWarning;
                app.FirstInstallmentDate = firstDueDate;
                app.DisbursedAt = DateTime.UtcNow;

                // Credit the approved amount to customer's account
                if (app.Account != null)
                {
                    app.Account.Balance += dto.ApprovedAmount;
                    app.Account.UpdatedAt = DateTime.UtcNow;

                    var disbursementTx = new Transaction
                    {
                        AccountId = app.Account.Id,
                        Type = TransactionType.Deposit,
                        Amount = dto.ApprovedAmount,
                        Timestamp = DateTime.UtcNow
                    };
                    _context.Transactions.Add(disbursementTx);
                }

                // Create Installments
                foreach (var item in scheduleItems)
                {
                    var installment = new LoanInstallment
                    {
                        LoanApplicationId = app.Id,
                        InstallmentNumber = item.InstallmentNumber,
                        DueDate = item.DueDate,
                        OpeningBalance = item.OpeningBalance,
                        PrincipalPortion = item.PrincipalPortion,
                        InterestPortion = item.InterestPortion,
                        TotalDue = item.TotalDue,
                        ClosingBalance = item.ClosingBalance,
                        Status = InstallmentStatus.Pending,
                        PaidAmount = 0.00m,
                        LateFeeApplied = 0.00m
                    };
                    _context.LoanInstallments.Add(installment);
                }

                // Add Audit Log
                var auditLog = new LoanAuditLog
                {
                    LoanApplicationId = app.Id,
                    Action = "LoanApproved",
                    FieldChanged = "Status",
                    OldValue = "Pending",
                    NewValue = "Approved",
                    PerformedBy = adminUsername,
                    PerformedAt = DateTime.UtcNow,
                    Note = $"Underwritten & Approved by {adminUsername}. Amount: ৳{dto.ApprovedAmount:N2}, Rate: {dto.InterestRate:F2}%, Tenure: {dto.TenureMonths}m, EMI: ৳{emi:N2}.{dtiWarning}"
                };
                _context.LoanAuditLogs.Add(auditLog);

                await _context.SaveChangesAsync();
                await transaction.CommitAsync();

                var appDto = MapToDto(app, app.User, app.Account);
                return (200, ApiResponse<LoanApplicationDto>.SuccessResponse(appDto, "Loan approved, funds disbursed, and repayment schedule created successfully."));
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                return (500, ApiResponse<LoanApplicationDto>.FailureResponse("Failed to approve loan application: " + ex.Message));
            }
        }

        public async Task<(int StatusCode, ApiResponse<LoanApplicationDto> Response)> RejectLoanAsync(
            int applicationId, string adminNote, string adminUsername)
        {
            var app = await _context.LoanApplications
                .Include(l => l.User)
                .Include(l => l.Account)
                .FirstOrDefaultAsync(l => l.Id == applicationId);

            if (app == null)
            {
                return (404, ApiResponse<LoanApplicationDto>.FailureResponse("Loan application not found."));
            }

            if (app.Status != "Pending")
            {
                return (400, ApiResponse<LoanApplicationDto>.FailureResponse($"Application is already in '{app.Status}' status."));
            }

            app.Status = "Rejected";
            app.ReviewedAt = DateTime.UtcNow;
            app.ReviewedBy = adminUsername;
            app.AdminNote = adminNote?.Trim();

            var auditLog = new LoanAuditLog
            {
                LoanApplicationId = app.Id,
                Action = "LoanRejected",
                FieldChanged = "Status",
                OldValue = "Pending",
                NewValue = "Rejected",
                PerformedBy = adminUsername,
                PerformedAt = DateTime.UtcNow,
                Note = $"Rejected by {adminUsername}. Reason: {adminNote}"
            };
            _context.LoanAuditLogs.Add(auditLog);

            await _context.SaveChangesAsync();

            var appDto = MapToDto(app, app.User, app.Account);
            return (200, ApiResponse<LoanApplicationDto>.SuccessResponse(appDto, "Loan application rejected."));
        }

        public async Task<(int StatusCode, ApiResponse<LoanPaymentDto> Response)> RecordPaymentAsync(
            RecordPaymentDto dto, string recordedBy)
        {
            var app = await _context.LoanApplications
                .Include(l => l.Installments)
                .Include(l => l.Account)
                .Include(l => l.User)
                .FirstOrDefaultAsync(l => l.Id == dto.LoanApplicationId);

            if (app == null)
            {
                return (404, ApiResponse<LoanPaymentDto>.FailureResponse("Loan application not found."));
            }

            if (app.Status != "Approved" && app.Status != "Disbursed")
            {
                return (400, ApiResponse<LoanPaymentDto>.FailureResponse($"Payments can only be recorded for active approved loans (current status: {app.Status})."));
            }

            if (dto.Amount <= 0)
            {
                return (400, ApiResponse<LoanPaymentDto>.FailureResponse("Payment amount must be greater than zero."));
            }

            // Find target installment
            LoanInstallment? targetInstallment = null;
            if (dto.InstallmentId.HasValue)
            {
                targetInstallment = app.Installments.FirstOrDefault(i => i.Id == dto.InstallmentId.Value);
            }
            else
            {
                // Next unpaid or overdue installment
                targetInstallment = app.Installments
                    .Where(i => i.Status == InstallmentStatus.Pending || i.Status == InstallmentStatus.Overdue || i.Status == InstallmentStatus.PartiallyPaid)
                    .OrderBy(i => i.InstallmentNumber)
                    .FirstOrDefault();
            }

            if (targetInstallment == null)
            {
                return (400, ApiResponse<LoanPaymentDto>.FailureResponse("All installments for this loan have already been satisfied."));
            }

            // Generate Payment Reference Number
            var refNumber = dto.ReferenceNumber;
            if (string.IsNullOrWhiteSpace(refNumber))
            {
                refNumber = $"PAY-{DateTime.UtcNow:yyyyMMdd}-{Guid.NewGuid().ToString("N").Substring(0, 8).ToUpper()}";
            }

            using var transaction = await _context.Database.BeginTransactionAsync();
            try
            {
                // If AccountDebit, deduct from user's account
                if (dto.Method == LoanPaymentMethod.AccountDebit && app.Account != null)
                {
                    if (app.Account.Balance < dto.Amount)
                    {
                        return (400, ApiResponse<LoanPaymentDto>.FailureResponse($"Insufficient account balance. Available: ৳{app.Account.Balance:N2}, Required: ৳{dto.Amount:N2}"));
                    }

                    app.Account.Balance -= dto.Amount;
                    app.Account.UpdatedAt = DateTime.UtcNow;

                    var debitTx = new Transaction
                    {
                        AccountId = app.Account.Id,
                        Type = TransactionType.Withdraw,
                        Amount = dto.Amount,
                        Timestamp = DateTime.UtcNow
                    };
                    _context.Transactions.Add(debitTx);
                }

                // Update Installment
                targetInstallment.PaidAmount += dto.Amount;
                targetInstallment.PaidAt = DateTime.UtcNow;
                targetInstallment.PaymentMethod = dto.Method.ToString();
                targetInstallment.TransactionReference = refNumber;

                var totalInstallmentDue = targetInstallment.TotalDue + targetInstallment.LateFeeApplied;
                if (targetInstallment.PaidAmount >= totalInstallmentDue - 0.01m)
                {
                    targetInstallment.Status = InstallmentStatus.Paid;
                }
                else
                {
                    targetInstallment.Status = InstallmentStatus.PartiallyPaid;
                }

                // Record Loan Payment
                var payment = new LoanPayment
                {
                    LoanApplicationId = app.Id,
                    InstallmentId = targetInstallment.Id,
                    Amount = dto.Amount,
                    PaymentDate = DateTime.UtcNow,
                    Method = dto.Method,
                    ReferenceNumber = refNumber,
                    RecordedBy = recordedBy,
                    Notes = dto.Notes
                };
                _context.LoanPayments.Add(payment);

                // Check if all installments are completed -> Close loan
                var remainingUnpaid = app.Installments.Any(i => i.Id != targetInstallment.Id && i.Status != InstallmentStatus.Paid && i.Status != InstallmentStatus.Waived);
                if (!remainingUnpaid && targetInstallment.Status == InstallmentStatus.Paid)
                {
                    app.Status = "Closed";
                    var closeAudit = new LoanAuditLog
                    {
                        LoanApplicationId = app.Id,
                        Action = "LoanClosed",
                        FieldChanged = "Status",
                        OldValue = "Approved",
                        NewValue = "Closed",
                        PerformedBy = recordedBy,
                        PerformedAt = DateTime.UtcNow,
                        Note = "All scheduled installments have been paid in full. Loan successfully closed."
                    };
                    _context.LoanAuditLogs.Add(closeAudit);
                }

                // Add payment audit log
                var payAudit = new LoanAuditLog
                {
                    LoanApplicationId = app.Id,
                    Action = "PaymentRecorded",
                    FieldChanged = "PaidAmount",
                    OldValue = (targetInstallment.PaidAmount - dto.Amount).ToString("F2"),
                    NewValue = targetInstallment.PaidAmount.ToString("F2"),
                    PerformedBy = recordedBy,
                    PerformedAt = DateTime.UtcNow,
                    Note = $"Payment of ৳{dto.Amount:N2} recorded for Installment #{targetInstallment.InstallmentNumber} via {dto.Method}. Ref: {refNumber}"
                };
                _context.LoanAuditLogs.Add(payAudit);

                await _context.SaveChangesAsync();
                await transaction.CommitAsync();

                var paymentDto = new LoanPaymentDto
                {
                    Id = payment.Id,
                    LoanApplicationId = payment.LoanApplicationId,
                    InstallmentId = payment.InstallmentId,
                    Amount = payment.Amount,
                    PaymentDate = payment.PaymentDate,
                    Method = payment.Method.ToString(),
                    ReferenceNumber = payment.ReferenceNumber,
                    RecordedBy = payment.RecordedBy,
                    Notes = payment.Notes
                };

                return (200, ApiResponse<LoanPaymentDto>.SuccessResponse(paymentDto, "Payment recorded successfully."));
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                return (500, ApiResponse<LoanPaymentDto>.FailureResponse("Failed to record payment: " + ex.Message));
            }
        }

        public async Task<LoanDetailsDto?> GetLoanDetailsAsync(string applicationNumber, int requestingUserId, bool isAdmin = false)
        {
            var query = _context.LoanApplications
                .Include(l => l.User)
                .Include(l => l.Account)
                .Include(l => l.Installments)
                .Include(l => l.Payments)
                .Include(l => l.AuditLogs)
                .Where(l => l.ApplicationNumber == applicationNumber);

            if (!isAdmin)
            {
                query = query.Where(l => l.UserId == requestingUserId);
            }

            var app = await query.FirstOrDefaultAsync();
            if (app == null) return null;

            return new LoanDetailsDto
            {
                Application = MapToDto(app, app.User, app.Account),
                Installments = app.Installments
                    .OrderBy(i => i.InstallmentNumber)
                    .Select(i => new LoanInstallmentDto
                    {
                        Id = i.Id,
                        LoanApplicationId = i.LoanApplicationId,
                        InstallmentNumber = i.InstallmentNumber,
                        DueDate = i.DueDate,
                        OpeningBalance = i.OpeningBalance,
                        PrincipalPortion = i.PrincipalPortion,
                        InterestPortion = i.InterestPortion,
                        TotalDue = i.TotalDue,
                        ClosingBalance = i.ClosingBalance,
                        Status = i.Status.ToString(),
                        PaidAmount = i.PaidAmount,
                        PaidAt = i.PaidAt,
                        PaymentMethod = i.PaymentMethod,
                        TransactionReference = i.TransactionReference,
                        LateFeeApplied = i.LateFeeApplied
                    }).ToList(),
                Payments = app.Payments
                    .OrderByDescending(p => p.PaymentDate)
                    .Select(p => new LoanPaymentDto
                    {
                        Id = p.Id,
                        LoanApplicationId = p.LoanApplicationId,
                        InstallmentId = p.InstallmentId,
                        Amount = p.Amount,
                        PaymentDate = p.PaymentDate,
                        Method = p.Method.ToString(),
                        ReferenceNumber = p.ReferenceNumber,
                        RecordedBy = p.RecordedBy,
                        Notes = p.Notes
                    }).ToList(),
                AuditLogs = app.AuditLogs
                    .OrderByDescending(a => a.PerformedAt)
                    .Select(a => new LoanAuditLogDto
                    {
                        Id = a.Id,
                        LoanApplicationId = a.LoanApplicationId,
                        Action = a.Action,
                        FieldChanged = a.FieldChanged,
                        OldValue = a.OldValue,
                        NewValue = a.NewValue,
                        PerformedBy = a.PerformedBy,
                        PerformedAt = a.PerformedAt,
                        Note = a.Note
                    }).ToList()
            };
        }

        public async Task<List<LoanInstallmentDto>> GetInstallmentsAsync(int applicationId)
        {
            var installments = await _context.LoanInstallments
                .Where(i => i.LoanApplicationId == applicationId)
                .OrderBy(i => i.InstallmentNumber)
                .ToListAsync();

            return installments.Select(i => new LoanInstallmentDto
            {
                Id = i.Id,
                LoanApplicationId = i.LoanApplicationId,
                InstallmentNumber = i.InstallmentNumber,
                DueDate = i.DueDate,
                OpeningBalance = i.OpeningBalance,
                PrincipalPortion = i.PrincipalPortion,
                InterestPortion = i.InterestPortion,
                TotalDue = i.TotalDue,
                ClosingBalance = i.ClosingBalance,
                Status = i.Status.ToString(),
                PaidAmount = i.PaidAmount,
                PaidAt = i.PaidAt,
                PaymentMethod = i.PaymentMethod,
                TransactionReference = i.TransactionReference,
                LateFeeApplied = i.LateFeeApplied
            }).ToList();
        }

        public async Task<List<LoanPaymentDto>> GetAllPaymentsForAdminAsync()
        {
            var payments = await _context.LoanPayments
                .OrderByDescending(p => p.PaymentDate)
                .ToListAsync();

            return payments.Select(p => new LoanPaymentDto
            {
                Id = p.Id,
                LoanApplicationId = p.LoanApplicationId,
                InstallmentId = p.InstallmentId,
                Amount = p.Amount,
                PaymentDate = p.PaymentDate,
                Method = p.Method.ToString(),
                ReferenceNumber = p.ReferenceNumber,
                RecordedBy = p.RecordedBy,
                Notes = p.Notes
            }).ToList();
        }

        public async Task<int> MarkOverdueInstallmentsAsync()
        {
            var now = DateTime.UtcNow;
            var overdueInstallments = await _context.LoanInstallments
                .Where(i => i.Status == InstallmentStatus.Pending && i.DueDate < now)
                .ToListAsync();

            if (!overdueInstallments.Any()) return 0;

            foreach (var inst in overdueInstallments)
            {
                inst.Status = InstallmentStatus.Overdue;
                if (inst.LateFeeApplied == 0m)
                {
                    // Apply standard 2% or ৳100 late fee
                    inst.LateFeeApplied = Math.Max(100m, Math.Round(inst.TotalDue * 0.02m, 2));
                }
            }

            await _context.SaveChangesAsync();
            return overdueInstallments.Count;
        }

        public async Task<AdminLoanStatsDto> GetAdminLoanStatsAsync()
        {
            var total = await _context.LoanApplications.CountAsync();
            var pending = await _context.LoanApplications.CountAsync(l => l.Status == "Pending");
            var approved = await _context.LoanApplications.CountAsync(l => l.Status == "Approved");
            var rejected = await _context.LoanApplications.CountAsync(l => l.Status == "Rejected");
            var totalApprovedAmount = await _context.LoanApplications
                .Where(l => l.Status == "Approved")
                .SumAsync(l => (decimal?)l.ApprovedAmount ?? (decimal?)l.RequestedAmount) ?? 0m;

            var activeLoans = await _context.LoanApplications
                .CountAsync(l => l.Status == "Approved" || l.Status == "Disbursed");

            var totalDisbursed = await _context.LoanApplications
                .Where(l => l.Status == "Approved" || l.Status == "Disbursed")
                .SumAsync(l => (decimal?)l.ApprovedAmount) ?? 0m;

            var totalPaid = await _context.LoanPayments.SumAsync(p => (decimal?)p.Amount) ?? 0m;
            var totalRepayable = await _context.LoanApplications
                .Where(l => l.Status == "Approved" || l.Status == "Disbursed")
                .SumAsync(l => (decimal?)l.TotalRepayable) ?? 0m;

            var totalOutstanding = Math.Max(0m, totalRepayable - totalPaid);

            var overdueCount = await _context.LoanInstallments
                .CountAsync(i => i.Status == InstallmentStatus.Overdue);

            return new AdminLoanStatsDto
            {
                TotalApplications = total,
                PendingCount = pending,
                ApprovedCount = approved,
                RejectedCount = rejected,
                TotalApprovedAmount = totalApprovedAmount,
                ActiveLoansCount = activeLoans,
                TotalDisbursedAmount = totalDisbursed,
                TotalOutstandingAmount = totalOutstanding,
                OverdueInstallmentsCount = overdueCount
            };
        }

        private static LoanApplicationDto MapToDto(LoanApplication app, User? user, Account? account)
        {
            return new LoanApplicationDto
            {
                Id = app.Id,
                ApplicationNumber = app.ApplicationNumber,
                UserId = app.UserId,
                CustomerName = user?.FullName ?? "Unknown",
                CustomerEmail = user?.Email ?? "N/A",
                CustomerPhone = user?.PhoneNumber ?? "N/A",
                AccountId = app.AccountId,
                AccountNumber = account?.AccountNumber ?? "N/A",
                LoanType = app.LoanType,
                RequestedAmount = app.RequestedAmount,
                EligibleAmount = app.EligibleAmount,
                EligibilityScore = app.EligibilityScore,
                EligibilityCategory = app.EligibilityCategory,
                Purpose = app.Purpose,
                MonthlyIncome = app.MonthlyIncome,
                Status = app.Status,
                AdminNote = app.AdminNote,
                CreatedAt = app.CreatedAt,
                ReviewedAt = app.ReviewedAt,
                ReviewedBy = app.ReviewedBy,
                ApprovedAmount = app.ApprovedAmount,
                FinalInterestRate = app.FinalInterestRate,
                FinalTenureMonths = app.FinalTenureMonths,
                FinalEmi = app.FinalEmi,
                TotalRepayable = app.TotalRepayable,
                IndicativeRate = app.IndicativeRate,
                RequestedTenureMonths = app.RequestedTenureMonths,
                FirstInstallmentDate = app.FirstInstallmentDate,
                DisbursedAt = app.DisbursedAt
            };
        }
    }
}
