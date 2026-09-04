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
        }
    }
}
