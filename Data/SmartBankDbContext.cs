using Microsoft.EntityFrameworkCore;
using SmartBank.Entities;

namespace SmartBank.Data
{
    public class SmartBankDbContext : DbContext
    {
        public SmartBankDbContext(DbContextOptions<SmartBankDbContext> options) : base(options)
        {
        }

        public DbSet<User> Users { get; set; } = null!;
        public DbSet<Account> Accounts { get; set; } = null!;
        public DbSet<RefreshToken> RefreshTokens { get; set; } = null!;
        public DbSet<Transaction> Transactions { get; set; } = null!;
        public DbSet<LoanApplication> LoanApplications { get; set; } = null!;
        public DbSet<LoanInstallment> LoanInstallments { get; set; } = null!;
        public DbSet<LoanPayment> LoanPayments { get; set; } = null!;
        public DbSet<LoanRatePolicy> LoanRatePolicies { get; set; } = null!;
        public DbSet<LoanAuditLog> LoanAuditLogs { get; set; } = null!;
        public DbSet<ExternalLogin> ExternalLogins { get; set; } = null!;
        public DbSet<OtpChallenge> OtpChallenges { get; set; } = null!;
        public DbSet<PendingTransaction> PendingTransactions { get; set; } = null!;
        public DbSet<TransferRequest> TransferRequests { get; set; } = null!;
        public DbSet<OtpVerification> OtpVerifications { get; set; } = null!;

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // User configuration
            modelBuilder.Entity<User>(entity =>
            {
                entity.HasKey(u => u.Id);
                entity.Property(u => u.FullName).IsRequired().HasMaxLength(100);
                entity.Property(u => u.Email).IsRequired().HasMaxLength(255);
                entity.Property(u => u.Username).IsRequired().HasMaxLength(50);
                entity.Property(u => u.PasswordHash).IsRequired();
                entity.Property(u => u.Role).IsRequired().HasMaxLength(50).HasDefaultValue("Customer");
                entity.Property(u => u.Status).IsRequired().HasMaxLength(50).HasDefaultValue("Active");
                entity.Property(u => u.IsEmailVerified).HasDefaultValue(false);
                entity.Property(u => u.IsFirstLogin).HasDefaultValue(true);
                entity.Property(u => u.FailedVaultAttempts).HasDefaultValue(0);

                // Indexes and Uniqueness
                entity.HasIndex(u => u.Username).IsUnique();
                entity.HasIndex(u => u.Email).IsUnique();
            });

            // Account configuration
            modelBuilder.Entity<Account>(entity =>
            {
                entity.HasKey(a => a.Id);
                entity.Property(a => a.AccountNumber).IsRequired().HasMaxLength(12);
                entity.Property(a => a.Balance).HasColumnType("decimal(18,2)").HasDefaultValue(0.00m);

                // Index and Uniqueness
                entity.HasIndex(a => a.AccountNumber).IsUnique();

                // Relationship
                entity.HasOne(a => a.User)
                    .WithMany(u => u.Accounts)
                    .HasForeignKey(a => a.UserId)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            // RefreshToken configuration
            modelBuilder.Entity<RefreshToken>(entity =>
            {
                entity.HasKey(rt => rt.Id);
                entity.Property(rt => rt.TokenHash).IsRequired().HasMaxLength(256);

                // Relationship
                entity.HasOne(rt => rt.User)
                    .WithMany(u => u.RefreshTokens)
                    .HasForeignKey(rt => rt.UserId)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            // Transaction configuration
            modelBuilder.Entity<Transaction>(entity =>
            {
                entity.HasKey(t => t.Id);
                entity.Property(t => t.Amount).HasColumnType("decimal(18,2)");

                // Relationship
                entity.HasOne(t => t.Account)
                    .WithMany(a => a.Transactions)
                    .HasForeignKey(t => t.AccountId)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            // LoanApplication configuration
            modelBuilder.Entity<LoanApplication>(entity =>
            {
                entity.HasKey(l => l.Id);
                entity.Property(l => l.ApplicationNumber).IsRequired().HasMaxLength(30);
                entity.Property(l => l.LoanType).IsRequired().HasMaxLength(50).HasDefaultValue("Personal");
                entity.Property(l => l.RequestedAmount).HasColumnType("decimal(18,2)");
                entity.Property(l => l.EligibleAmount).HasColumnType("decimal(18,2)");
                entity.Property(l => l.EligibilityCategory).IsRequired().HasMaxLength(50).HasDefaultValue("Not Eligible");
                entity.Property(l => l.Purpose).IsRequired().HasMaxLength(500);
                entity.Property(l => l.MonthlyIncome).HasColumnType("decimal(18,2)");
                entity.Property(l => l.Status).IsRequired().HasMaxLength(50).HasDefaultValue("Pending");
                entity.Property(l => l.AdminNote).HasMaxLength(1000);
                entity.Property(l => l.ReviewedBy).HasMaxLength(100);

                entity.Property(l => l.ApprovedAmount).HasColumnType("decimal(18,2)");
                entity.Property(l => l.FinalInterestRate).HasColumnType("decimal(5,2)");
                entity.Property(l => l.FinalEmi).HasColumnType("decimal(18,2)");
                entity.Property(l => l.TotalRepayable).HasColumnType("decimal(18,2)");
                entity.Property(l => l.IndicativeRate).HasColumnType("decimal(5,2)");

                // Indexes
                entity.HasIndex(l => l.ApplicationNumber).IsUnique();
                entity.HasIndex(l => l.UserId);
                entity.HasIndex(l => l.AccountId);
                entity.HasIndex(l => l.Status);

                // Relationships
                entity.HasOne(l => l.User)
                    .WithMany(u => u.LoanApplications)
                    .HasForeignKey(l => l.UserId)
                    .OnDelete(DeleteBehavior.Cascade);

                entity.HasOne(l => l.Account)
                    .WithMany(a => a.LoanApplications)
                    .HasForeignKey(l => l.AccountId)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            // LoanInstallment configuration
            modelBuilder.Entity<LoanInstallment>(entity =>
            {
                entity.HasKey(i => i.Id);
                entity.Property(i => i.OpeningBalance).HasColumnType("decimal(18,2)");
                entity.Property(i => i.PrincipalPortion).HasColumnType("decimal(18,2)");
                entity.Property(i => i.InterestPortion).HasColumnType("decimal(18,2)");
                entity.Property(i => i.TotalDue).HasColumnType("decimal(18,2)");
                entity.Property(i => i.ClosingBalance).HasColumnType("decimal(18,2)");
                entity.Property(i => i.PaidAmount).HasColumnType("decimal(18,2)").HasDefaultValue(0.00m);
                entity.Property(i => i.LateFeeApplied).HasColumnType("decimal(18,2)").HasDefaultValue(0.00m);
                entity.Property(i => i.Status).HasConversion<string>().HasMaxLength(50).HasDefaultValue(InstallmentStatus.Pending);
                entity.Property(i => i.PaymentMethod).HasMaxLength(50);
                entity.Property(i => i.TransactionReference).HasMaxLength(100);

                // Indexes
                entity.HasIndex(i => i.LoanApplicationId).HasDatabaseName("IX_LoanInstallments_LoanApplicationId");
                entity.HasIndex(i => new { i.DueDate, i.Status }).HasDatabaseName("IX_LoanInstallments_DueDate_Status");

                // Relationships
                entity.HasOne(i => i.LoanApplication)
                    .WithMany(l => l.Installments)
                    .HasForeignKey(i => i.LoanApplicationId)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            // LoanPayment configuration
            modelBuilder.Entity<LoanPayment>(entity =>
            {
                entity.HasKey(p => p.Id);
                entity.Property(p => p.Amount).HasColumnType("decimal(18,2)");
                entity.Property(p => p.Method).HasConversion<string>().HasMaxLength(50).HasDefaultValue(LoanPaymentMethod.AccountDebit);
                entity.Property(p => p.ReferenceNumber).IsRequired().HasMaxLength(100);
                entity.Property(p => p.RecordedBy).IsRequired().HasMaxLength(100).HasDefaultValue("SYSTEM");
                entity.Property(p => p.Notes).HasMaxLength(1000);

                // Indexes
                entity.HasIndex(p => p.ReferenceNumber).IsUnique().HasDatabaseName("IX_LoanPayments_ReferenceNumber");
                entity.HasIndex(p => p.LoanApplicationId);
                entity.HasIndex(p => p.InstallmentId);

                // Relationships
                entity.HasOne(p => p.LoanApplication)
                    .WithMany(l => l.Payments)
                    .HasForeignKey(p => p.LoanApplicationId)
                    .OnDelete(DeleteBehavior.Cascade);

                entity.HasOne(p => p.Installment)
                    .WithMany(i => i.Payments)
                    .HasForeignKey(p => p.InstallmentId)
                    .OnDelete(DeleteBehavior.SetNull);
            });

            // LoanRatePolicy configuration & Seeding
            modelBuilder.Entity<LoanRatePolicy>(entity =>
            {
                entity.HasKey(r => r.Id);
                entity.Property(r => r.Category).IsRequired().HasMaxLength(50);
                entity.Property(r => r.BaseAnnualRate).HasColumnType("decimal(5,2)");
                entity.Property(r => r.IsActive).HasDefaultValue(true);

                // Seed 3 baseline underwriting policy bands
                entity.HasData(
                    new LoanRatePolicy
                    {
                        Id = 1,
                        Category = "Excellent",
                        MinScore = 80,
                        MaxScore = 100,
                        BaseAnnualRate = 8.00m,
                        MinTenureMonths = 6,
                        MaxTenureMonths = 60,
                        IsActive = true,
                        EffectiveFrom = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)
                    },
                    new LoanRatePolicy
                    {
                        Id = 2,
                        Category = "Good",
                        MinScore = 65,
                        MaxScore = 79,
                        BaseAnnualRate = 10.50m,
                        MinTenureMonths = 6,
                        MaxTenureMonths = 48,
                        IsActive = true,
                        EffectiveFrom = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)
                    },
                    new LoanRatePolicy
                    {
                        Id = 3,
                        Category = "ReviewRequired",
                        MinScore = 50,
                        MaxScore = 64,
                        BaseAnnualRate = 13.00m,
                        MinTenureMonths = 6,
                        MaxTenureMonths = 36,
                        IsActive = true,
                        EffectiveFrom = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)
                    }
                );
            });

            // LoanAuditLog configuration
            modelBuilder.Entity<LoanAuditLog>(entity =>
            {
                entity.HasKey(a => a.Id);
                entity.Property(a => a.Action).IsRequired().HasMaxLength(100);
                entity.Property(a => a.FieldChanged).HasMaxLength(100);
                entity.Property(a => a.OldValue).HasMaxLength(500);
                entity.Property(a => a.NewValue).HasMaxLength(500);
                entity.Property(a => a.PerformedBy).IsRequired().HasMaxLength(100);
                entity.Property(a => a.Note).HasMaxLength(1000);

                // Index
                entity.HasIndex(a => new { a.LoanApplicationId, a.PerformedAt }).HasDatabaseName("IX_LoanAuditLogs_LoanApplicationId_PerformedAt");

                // Relationship
                entity.HasOne(a => a.LoanApplication)
                    .WithMany(l => l.AuditLogs)
                    .HasForeignKey(a => a.LoanApplicationId)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            // ExternalLogin configuration
            modelBuilder.Entity<ExternalLogin>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.Provider).IsRequired().HasMaxLength(50);
                entity.Property(e => e.ProviderUserId).IsRequired().HasMaxLength(255);
                entity.Property(e => e.Email).IsRequired().HasMaxLength(255);

                // Indexes
                entity.HasIndex(e => new { e.Provider, e.ProviderUserId }).IsUnique();
                entity.HasIndex(e => e.UserId);

                // Relationship
                entity.HasOne(e => e.User)
                    .WithMany(u => u.ExternalLogins)
                    .HasForeignKey(e => e.UserId)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            // PendingTransaction configuration
            modelBuilder.Entity<PendingTransaction>(entity =>
            {
                entity.HasKey(pt => pt.Id);
                entity.Property(pt => pt.Amount).HasColumnType("decimal(18,2)");
                entity.Property(pt => pt.TransactionType).IsRequired().HasMaxLength(50);
                entity.Property(pt => pt.Status).IsRequired().HasMaxLength(50).HasDefaultValue("PendingOtp");

                entity.HasIndex(pt => pt.UserId);
                entity.HasIndex(pt => pt.Status);

                entity.HasOne(pt => pt.User)
                    .WithMany()
                    .HasForeignKey(pt => pt.UserId)
                    .OnDelete(DeleteBehavior.Cascade);

                entity.HasOne(pt => pt.Account)
                    .WithMany()
                    .HasForeignKey(pt => pt.AccountId)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            // OtpChallenge configuration
            modelBuilder.Entity<OtpChallenge>(entity =>
            {
                entity.HasKey(o => o.Id);
                entity.Property(o => o.TransactionType).IsRequired().HasMaxLength(50);
                entity.Property(o => o.HashedOtp).IsRequired().HasMaxLength(256);
                entity.Property(o => o.TransactionAmount).HasColumnType("decimal(18,2)");
                entity.Property(o => o.Status).IsRequired().HasMaxLength(50).HasDefaultValue("Pending");

                // Indexes
                entity.HasIndex(o => new { o.UserId, o.TransactionType, o.TransactionId });
                entity.HasIndex(o => o.ExpiresAt);

                // Relationship
                entity.HasOne(o => o.User)
                    .WithMany(u => u.OtpChallenges)
                    .HasForeignKey(o => o.UserId)
                    .OnDelete(DeleteBehavior.Cascade);

                entity.HasOne(o => o.PendingTransaction)
                    .WithMany()
                    .HasForeignKey(o => o.TransactionId)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            // TransferRequest configuration
            modelBuilder.Entity<TransferRequest>(entity =>
            {
                entity.HasKey(tr => tr.Id);
                entity.Property(tr => tr.Amount).HasColumnType("decimal(18,2)");
                entity.Property(tr => tr.Status).IsRequired().HasMaxLength(50).HasDefaultValue(TransferRequestStatus.PendingOtp);
                entity.Property(tr => tr.Memo).HasMaxLength(200);

                // Indexes
                entity.HasIndex(tr => tr.UserId);
                entity.HasIndex(tr => tr.Status);

                // Relationships
                entity.HasOne(tr => tr.User)
                    .WithMany(u => u.TransferRequests)
                    .HasForeignKey(tr => tr.UserId)
                    .OnDelete(DeleteBehavior.Restrict);

                entity.HasOne(tr => tr.SourceAccount)
                    .WithMany()
                    .HasForeignKey(tr => tr.SourceAccountId)
                    .OnDelete(DeleteBehavior.Restrict);

                entity.HasOne(tr => tr.DestinationAccount)
                    .WithMany()
                    .HasForeignKey(tr => tr.DestinationAccountId)
                    .OnDelete(DeleteBehavior.Restrict);
            });

            // OtpVerification configuration
            modelBuilder.Entity<OtpVerification>(entity =>
            {
                entity.HasKey(o => o.Id);
                entity.Property(o => o.Email).IsRequired().HasMaxLength(255);
                entity.Property(o => o.CodeHash).IsRequired().HasMaxLength(256);
                entity.Property(o => o.Salt).IsRequired().HasMaxLength(64);
                entity.Property(o => o.AttemptCount).HasDefaultValue(0);
                entity.Property(o => o.IsUsed).HasDefaultValue(false);

                // Indexes
                entity.HasIndex(o => new { o.Email, o.IsUsed, o.ExpiresAtUtc });

                // Relationships
                entity.HasOne(o => o.User)
                    .WithMany()
                    .HasForeignKey(o => o.UserId)
                    .OnDelete(DeleteBehavior.SetNull);
            });
        }
    }
}
