# 🏦 SmartBank — Enterprise Digital Banking & Financial System

![.NET 10](https://img.shields.io/badge/.NET-10.0-512BD4?logo=dotnet)
![PostgreSQL](https://img.shields.io/badge/PostgreSQL-Neon%20Cloud-4169E1?logo=postgresql)
![ASP.NET Core](https://img.shields.io/badge/ASP.NET%20Core-MVC%20%26%20Web%20API-00599C)
![License](https://img.shields.io/badge/Security-OAuth2.0%20%2b%20MFA%20OTP-green)

**SmartBank** is a modern, full-stack digital banking platform built with **ASP.NET Core MVC**, **RESTful Web APIs**, **Entity Framework Core**, **PostgreSQL (Neon Cloud DB)**, and a dedicated **C# WinForms Desktop Client**. 

The system provides robust online banking capabilities including NID-verified customer onboarding, Google OAuth 2.0 authentication, mandatory multi-factor email OTP authorization for fund transfers, automated loan eligibility scoring, and comprehensive admin compliance auditing.

---

## 📸 Key Features & Capabilities

### 🔒 Security & Authentication
- **Hybrid Authentication Engine**: Supports Cookie-based sessions for the Web Portal and JWT Bearer Tokens for RESTful API clients and the Desktop application.
- **Google OAuth 2.0 Integration**: Fast and secure single sign-on (SSO) with auto-linked profiles.
- **Mandatory Transfer Authorization (MFA/2FA)**: Every financial transaction requires a 6-digit cryptographic OTP dispatched to the customer's verified email, hashed using SHA-256 before storage.
- **Identity & Compliance Control**: New self-registered customers undergo mandatory Admin NID verification before account activation.
- **Password Security**: Passwords hashed with BCrypt. Force password change policy enforced on initial sign-in for system-generated credentials.
- **Rate Limiting & Lockout**: Built-in memory caching protects against brute-force attacks and rate limit abuse.

### 💳 Banking Operations & Transactions
- **Double-Entry Ledger Integrity**: Funds transfers between accounts are executed atomically within database transactions (`TransferOut` and `TransferIn` entries).
- **Transaction Receipt & History**: Instant PDF/HTML printable transaction receipts with unique reference IDs.
- **Account Overview & Analytics**: Real-time balance tracking, recent transactions, and activity streams.

### 🏦 Loan Operations & Credit Scoring
- **Automated Credit Eligibility Engine**: Evaluates user income, employment status, credit history, and current balances to calculate loan eligibility scores.
- **Loan Applications**: Customers can apply for loans directly from the portal.
- **Admin Approval Workflow**: Admins can inspect credit scores, approve/reject loan requests, and trigger automatic funds disbursement upon approval.

### 💻 Dual Interface Support
- **MVC Web Application**: Responsive, high-performance web interface designed with dark glassmorphism styling.
- **WinForms Desktop Client (`SmartBank.Client`)**: Native Windows desktop application for teller/client desktop operations interacting via REST API.

---

## 🛠 Tech Stack & Architecture

- **Backend Framework**: ASP.NET Core 9.0 / 10.0 (C#)
- **Database**: PostgreSQL (Serverless Cloud via Neon DB / Local PostgreSQL)
- **ORM**: Entity Framework Core 9 (Npgsql Provider)
- **Authentication**: JWT Bearer, Cookie Authentication, Google OAuth 2.0
- **Email Service**: MailKit & MimeKit SMTP Integration
- **Security & Hashing**: BCrypt.Net, SHA-256 OTP hashing
- **API Documentation**: Swagger / Swashbuckle OpenAPI
- **Desktop Application**: WinForms (.NET 9.0 C#)

---

## 📁 Repository Structure

```
SmartBank/
├── Controllers/                 # Web MVC & RESTful API Controllers
│   ├── AccountController.cs     # Login, Profile, NID Registration
│   ├── AdminController.cs       # Customer Verification, Admin Dashboard
│   ├── ApiAdminController.cs    # REST Admin Endpoints for WinForms
│   ├── AuthController.cs        # JWT Login & OAuth REST Endpoints
│   ├── LoanController.cs        # Loan Application & Admin Reviews
│   ├── TransactionController.cs # Transfers, Receipts, & History
│   └── ...
├── Data/                        # EF Core DbContext & Migrations
│   └── SmartBankDbContext.cs
├── Entities/                    # Database Entities (User, Account, Transaction, Loan)
├── Services/                    # Business Logic & Infrastructure Services
│   ├── AuthService.cs           # User Auth & JWT Token Issuance
│   ├── LoanEligibilityService.cs# Automated Loan Credit Scoring
│   ├── OtpService.cs            # Cryptographic OTP Generation & Verification
│   ├── SmtpEmailService.cs      # MailKit Email Dispatching
│   ├── TransferService.cs       # Atomic Transfer Ledger Execution
│   └── ...
├── Views/                       # Razor Views (MVC UI)
├── SmartBank.Client/            # WinForms Desktop Client Application
├── appsettings.json             # Production Configuration Template (Git Tracked)
├── appsettings.Development.json # Local Development Settings (Git Ignored)
├── GOOGLE_AUTH_SETUP.md         # Detailed Google OAuth 2.0 Setup Guide
└── README.md
```

---

## 🚀 Getting Started

### Prerequisites
- [.NET 9.0 SDK](https://dotnet.microsoft.com/download) or [.NET 10.0 SDK](https://dotnet.microsoft.com/download)
- PostgreSQL Database (Local installation or a free cloud database on [Neon.tech](https://neon.tech/))

---

### ⚙️ Environment Setup & Database Configuration

1. **Clone the Repository**:
   ```bash
   git clone https://github.com/faiyazfardin/SmartBank.git
   cd SmartBank
   ```

2. **Configure Database Connection & Secrets**:
   To keep database credentials and API keys out of GitHub, sensitive secrets belong in **`appsettings.Development.json`** or **.NET User Secrets** (both git-ignored).

   Create or edit `appsettings.Development.json` in the root folder:
   ```json
   {
     "ConnectionStrings": {
       "DefaultConnection": "Host=ep-your-neon-host.neon.tech;Database=neondb;Username=neondb_owner;Password=YOUR_NEON_PASSWORD;SSL Mode=Require;Trust Server Certificate=true;"
     },
     "Jwt": {
       "Key": "YOUR_JWT_SUPER_SECRET_KEY_MINIMUM_32_CHARS!",
       "Issuer": "SmartBankAPI",
       "Audience": "SmartBankClient"
     },
     "Smtp": {
       "Host": "smtp.gmail.com",
       "Port": 587,
       "UseStartTls": true,
       "Username": "yourbankemail@gmail.com",
       "Password": "your-16-char-app-password",
       "FromEmail": "yourbankemail@gmail.com",
       "FromName": "SmartBank Security"
     }
   }
   ```

3. **Apply Database Migrations**:
   Run Entity Framework Core migrations to construct the PostgreSQL schema:
   ```bash
   dotnet ef database update
   ```

4. **Run the Web Application**:
   ```bash
   dotnet run
   ```
   Open your browser and navigate to `http://localhost:5096` or `https://localhost:7143`.

5. **Swagger API Documentation**:
   Access interactive REST API documentation at:
   `https://localhost:7143/swagger`

---

## 🔑 Google OAuth 2.0 Setup

For full instructions on establishing Google Single Sign-On (SSO):
Refer to the **[GOOGLE_AUTH_SETUP.md](GOOGLE_AUTH_SETUP.md)** guide included in this workspace.

---

## 💻 Running the Desktop Client (`SmartBank.Client`)

To launch the native Windows WinForms Client:
```bash
cd SmartBank.Client
dotnet run
```
The desktop application connects to the running REST API web backend to allow admin and teller operations.

---

## 🛡️ Security Highlights

- **Zero Secrets in Repository**: Connection strings and credentials are excluded from source control using `.gitignore` and `appsettings.Development.json`.
- **Cryptographic Hashing**: All passwords use BCrypt; verification codes use SHA-256 hashing.
- **SQL Injection & XSS Shield**: Full parameterized queries through EF Core and encoded Razor view rendering.

---

## 📜 License

Distributed under the MIT License. See `LICENSE` for details.
