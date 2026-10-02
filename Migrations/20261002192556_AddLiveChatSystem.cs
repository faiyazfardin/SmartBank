using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace SmartBank.Migrations
{
    /// <inheritdoc />
    public partial class AddLiveChatSystem : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<int>(
                name: "FailedLoginCount",
                table: "Users",
                type: "integer",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "integer",
                oldDefaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "EmailVerifiedAt",
                table: "Users",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsEmailVerified",
                table: "Users",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "MustChangePasswordOnNextLogin",
                table: "Users",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "NidNumber",
                table: "Users",
                type: "character varying(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "TemporaryPasswordIssuedAtUtc",
                table: "Users",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AlterColumn<bool>(
                name: "IsActive",
                table: "Accounts",
                type: "boolean",
                nullable: false,
                oldClrType: typeof(bool),
                oldType: "boolean",
                oldDefaultValue: true);

            migrationBuilder.CreateTable(
                name: "ChatConnections",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UserId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    ConnectionId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Role = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    ConnectedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChatConnections", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ChatConversations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    StartedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LastMessageAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastMessagePreview = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChatConversations", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ExternalLogins",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UserId = table.Column<int>(type: "integer", nullable: false),
                    Provider = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    ProviderUserId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    Email = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LastLoginAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExternalLogins", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ExternalLogins_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "LoanApplications",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ApplicationNumber = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    UserId = table.Column<int>(type: "integer", nullable: false),
                    AccountId = table.Column<int>(type: "integer", nullable: false),
                    LoanType = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false, defaultValue: "Personal"),
                    RequestedAmount = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    EligibleAmount = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    EligibilityScore = table.Column<int>(type: "integer", nullable: false),
                    EligibilityCategory = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false, defaultValue: "Not Eligible"),
                    Purpose = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    MonthlyIncome = table.Column<decimal>(type: "numeric(18,2)", nullable: true),
                    Status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false, defaultValue: "Pending"),
                    AdminNote = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ReviewedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ReviewedBy = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LoanApplications", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LoanApplications_Accounts_AccountId",
                        column: x => x.AccountId,
                        principalTable: "Accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_LoanApplications_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "OtpVerifications",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<int>(type: "integer", nullable: true),
                    Email = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    CodeHash = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Salt = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ExpiresAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    AttemptCount = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    IsUsed = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    Purpose = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OtpVerifications", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OtpVerifications_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "PendingTransactions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<int>(type: "integer", nullable: false),
                    AccountId = table.Column<int>(type: "integer", nullable: false),
                    TransactionType = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    RecipientAccount = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    BillerName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Reference = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false, defaultValue: "PendingOtp"),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CompletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    TrackingId = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PendingTransactions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PendingTransactions_Accounts_AccountId",
                        column: x => x.AccountId,
                        principalTable: "Accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PendingTransactions_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ChatMessages",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ConversationId = table.Column<Guid>(type: "uuid", nullable: false),
                    SenderId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    SenderRole = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    MessageText = table.Column<string>(type: "text", nullable: true),
                    AttachmentUrl = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    SentAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    IsRead = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChatMessages", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ChatMessages_ChatConversations_ConversationId",
                        column: x => x.ConversationId,
                        principalTable: "ChatConversations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "OtpChallenges",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<int>(type: "integer", nullable: false),
                    HashedOtp = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    TransactionType = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    TransactionId = table.Column<Guid>(type: "uuid", nullable: false),
                    TransactionAmount = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    TargetInfo = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    IssuedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LastSentAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsUsed = table.Column<bool>(type: "boolean", nullable: false),
                    UsedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    AttemptCount = table.Column<int>(type: "integer", nullable: false),
                    MaxAttempts = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false, defaultValue: "Pending")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OtpChallenges", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OtpChallenges_PendingTransactions_TransactionId",
                        column: x => x.TransactionId,
                        principalTable: "PendingTransactions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_OtpChallenges_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TransferRequests",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UserId = table.Column<int>(type: "integer", nullable: false),
                    SourceAccountId = table.Column<int>(type: "integer", nullable: false),
                    DestinationAccountId = table.Column<int>(type: "integer", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    Memo = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false, defaultValue: "PendingOtp"),
                    OtpChallengeId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CompletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TransferRequests", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TransferRequests_Accounts_DestinationAccountId",
                        column: x => x.DestinationAccountId,
                        principalTable: "Accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TransferRequests_Accounts_SourceAccountId",
                        column: x => x.SourceAccountId,
                        principalTable: "Accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TransferRequests_OtpChallenges_OtpChallengeId",
                        column: x => x.OtpChallengeId,
                        principalTable: "OtpChallenges",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_TransferRequests_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ChatConnections_ConnectionId",
                table: "ChatConnections",
                column: "ConnectionId");

            migrationBuilder.CreateIndex(
                name: "IX_ChatConnections_UserId",
                table: "ChatConnections",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_ChatConversations_UserId",
                table: "ChatConversations",
                column: "UserId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ChatMessages_ConversationId",
                table: "ChatMessages",
                column: "ConversationId");

            migrationBuilder.CreateIndex(
                name: "IX_ChatMessages_SentAt",
                table: "ChatMessages",
                column: "SentAt");

            migrationBuilder.CreateIndex(
                name: "IX_ExternalLogins_Provider_ProviderUserId",
                table: "ExternalLogins",
                columns: new[] { "Provider", "ProviderUserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ExternalLogins_UserId",
                table: "ExternalLogins",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_LoanApplications_AccountId",
                table: "LoanApplications",
                column: "AccountId");

            migrationBuilder.CreateIndex(
                name: "IX_LoanApplications_ApplicationNumber",
                table: "LoanApplications",
                column: "ApplicationNumber",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LoanApplications_Status",
                table: "LoanApplications",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_LoanApplications_UserId",
                table: "LoanApplications",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_OtpChallenges_ExpiresAt",
                table: "OtpChallenges",
                column: "ExpiresAt");

            migrationBuilder.CreateIndex(
                name: "IX_OtpChallenges_TransactionId",
                table: "OtpChallenges",
                column: "TransactionId");

            migrationBuilder.CreateIndex(
                name: "IX_OtpChallenges_UserId_TransactionType_TransactionId",
                table: "OtpChallenges",
                columns: new[] { "UserId", "TransactionType", "TransactionId" });

            migrationBuilder.CreateIndex(
                name: "IX_OtpVerifications_Email_IsUsed_ExpiresAtUtc",
                table: "OtpVerifications",
                columns: new[] { "Email", "IsUsed", "ExpiresAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_OtpVerifications_UserId",
                table: "OtpVerifications",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_PendingTransactions_AccountId",
                table: "PendingTransactions",
                column: "AccountId");

            migrationBuilder.CreateIndex(
                name: "IX_PendingTransactions_Status",
                table: "PendingTransactions",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_PendingTransactions_UserId",
                table: "PendingTransactions",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_TransferRequests_DestinationAccountId",
                table: "TransferRequests",
                column: "DestinationAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_TransferRequests_OtpChallengeId",
                table: "TransferRequests",
                column: "OtpChallengeId");

            migrationBuilder.CreateIndex(
                name: "IX_TransferRequests_SourceAccountId",
                table: "TransferRequests",
                column: "SourceAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_TransferRequests_Status",
                table: "TransferRequests",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_TransferRequests_UserId",
                table: "TransferRequests",
                column: "UserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ChatConnections");

            migrationBuilder.DropTable(
                name: "ChatMessages");

            migrationBuilder.DropTable(
                name: "ExternalLogins");

            migrationBuilder.DropTable(
                name: "LoanApplications");

            migrationBuilder.DropTable(
                name: "OtpVerifications");

            migrationBuilder.DropTable(
                name: "TransferRequests");

            migrationBuilder.DropTable(
                name: "ChatConversations");

            migrationBuilder.DropTable(
                name: "OtpChallenges");

            migrationBuilder.DropTable(
                name: "PendingTransactions");

            migrationBuilder.DropColumn(
                name: "EmailVerifiedAt",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "IsEmailVerified",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "MustChangePasswordOnNextLogin",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "NidNumber",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "TemporaryPasswordIssuedAtUtc",
                table: "Users");

            migrationBuilder.AlterColumn<int>(
                name: "FailedLoginCount",
                table: "Users",
                type: "integer",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "integer");

            migrationBuilder.AlterColumn<bool>(
                name: "IsActive",
                table: "Accounts",
                type: "boolean",
                nullable: false,
                defaultValue: true,
                oldClrType: typeof(bool),
                oldType: "boolean");
        }
    }
}
