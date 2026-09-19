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
        public DbSet<ExternalLogin> ExternalLogins { get; set; } = null!;
        public DbSet<OtpChallenge> OtpChallenges { get; set; } = null!;
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

            // OtpChallenge configuration
            modelBuilder.Entity<OtpChallenge>(entity =>
            {
                entity.HasKey(o => o.Id);
                entity.Property(o => o.Purpose).IsRequired().HasMaxLength(50);
                entity.Property(o => o.ReferenceId).HasMaxLength(100);
                entity.Property(o => o.CodeHash).IsRequired().HasMaxLength(256);

                // Indexes
                entity.HasIndex(o => new { o.UserId, o.Purpose, o.ReferenceId });

                // Relationship
                entity.HasOne(o => o.User)
                    .WithMany(u => u.OtpChallenges)
                    .HasForeignKey(o => o.UserId)
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

                entity.HasOne(tr => tr.OtpChallenge)
                    .WithMany()
                    .HasForeignKey(tr => tr.OtpChallengeId)
                    .OnDelete(DeleteBehavior.SetNull);
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
