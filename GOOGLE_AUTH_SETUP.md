# Google OAuth 2.0 & Verified Email OTP Setup Guide

This guide provides step-by-step instructions to configure **Real Google OAuth 2.0 Authentication** and **Mandatory Email OTP Transfer Security** for **SmartBank**.

---

## 1. Google Cloud Console Setup

### Step 1: Create a Google Cloud Project
1. Go to [Google Cloud Console](https://console.cloud.google.com/).
2. Click the project dropdown at the top navigation bar and select **New Project**.
3. Set **Project Name** to `SmartBank` and click **Create**.

### Step 2: Configure OAuth Consent Screen
1. In the left navigation menu, go to **APIs & Services** $\rightarrow$ **OAuth consent screen**.
2. Select **External** and click **Create**.
3. Fill in the **App information**:
   - **App name**: `SmartBank`
   - **User support email**: Your personal or admin Gmail address.
   - **Developer contact information**: Your email address.
4. Click **Save and Continue**.
5. Under **Scopes**, click **Add or Remove Scopes**:
   - Select: `openid`, `.../auth/userinfo.email`, `.../auth/userinfo.profile`.
   - Click **Update** $\rightarrow$ Click **Save and Continue**.
6. Under **Test users**:
   - Click **+ Add Users** and enter any Gmail addresses you will use for testing.
   - Click **Save and Continue**.

### Step 3: Create OAuth 2.0 Client Credentials
1. In the left sidebar, click **Credentials** $\rightarrow$ **+ Create Credentials** $\rightarrow$ **OAuth client ID**.
2. **Application type**: Select `Web application`.
3. **Name**: `SmartBank Web Portal`.
4. Under **Authorized JavaScript origins**, add:
   - `https://localhost:7143` *(or your local HTTPS port from `Properties/launchSettings.json`)*
   - `http://localhost:5000`
5. Under **Authorized redirect URIs**, add:
   - `https://localhost:7143/signin-google`
   - `https://localhost:7143/Account/GoogleCallback`
   *(For production deployment, also add your live production URL, e.g., `https://smartbank.yourdomain.com/signin-google`)*.
6. Click **Create** and copy your **Client ID** and **Client Secret**.

---

## 2. SMTP Email Configuration (Free Gmail SMTP)

To dispatch 6-digit verification codes and mandatory transfer OTPs via Gmail:

1. Enable **2-Step Verification** on your Google Account:
   - Visit [Google Account Security](https://myaccount.google.com/security).
2. Generate an **App Password**:
   - Under *How you sign in to Google*, click **2-Step Verification** $\rightarrow$ Scroll to **App passwords**.
   - Create an App Password with name: `SmartBank`.
   - Copy the generated 16-character password (e.g. `abcd efgh ijkl mnop`).

---

## 3. Configuring Secrets in SmartBank

### Using User Secrets (Recommended for Local Dev)
Run in the project directory:
```bash
dotnet user-secrets set "Authentication:Google:ClientId" "YOUR_GOOGLE_CLIENT_ID.apps.googleusercontent.com"
dotnet user-secrets set "Authentication:Google:ClientSecret" "YOUR_GOOGLE_CLIENT_SECRET"
dotnet user-secrets set "Smtp:Username" "yourbankemail@gmail.com"
dotnet user-secrets set "Smtp:Password" "your-16-char-app-password"
dotnet user-secrets set "Smtp:FromEmail" "yourbankemail@gmail.com"
```

### Or update `appsettings.Development.json`
```json
{
  "Authentication": {
    "Google": {
      "ClientId": "YOUR_GOOGLE_CLIENT_ID.apps.googleusercontent.com",
      "ClientSecret": "YOUR_GOOGLE_CLIENT_SECRET"
    }
  },
  "Smtp": {
    "Host": "smtp.gmail.com",
    "Port": 587,
    "Username": "yourbankemail@gmail.com",
    "Password": "your-16-char-app-password",
    "FromEmail": "yourbankemail@gmail.com",
    "FromName": "SmartBank Security"
  },
  "Email": {
    "SenderEmail": "security@smartbank.com",
    "SenderName": "SmartBank Security"
  },
  "Otp": {
    "Length": 6,
    "ExpiryMinutes": 5,
    "MaxAttempts": 5,
    "ResendCooldownSeconds": 60
  }
}
```

---

## 4. End-to-End Workflow & Security Rules

1. **Google OAuth & Inheritance**:
   - User signs in with Google $\rightarrow$ Account is authenticated.
   - New users complete their profile (NID, Phone, Username) $\rightarrow$ Account is created with `Status = "Pending"` for Admin verification.
   - Google verified email is automatically marked as `IsEmailVerified = true`.
2. **Mandatory Transfer OTP**:
   - Customers initiate a transfer on `/Transaction/Transfer` or via `/api/transfers`.
   - Funds are **NOT** moved immediately; a `TransferRequest` is created in `PendingOtp` state.
   - A 6-digit cryptographic OTP is generated, hashed with SHA-256, and emailed to the customer.
   - Customer submits the OTP on `/Transaction/VerifyTransferOtp` or desktop client `TransferOtpDialog`.
   - The transfer is atomically executed within a database transaction; debit, credit, and double-entry transaction records (`TransferOut`, `TransferIn`) are committed simultaneously.
