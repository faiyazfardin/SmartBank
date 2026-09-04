using System;
using System.Windows.Forms;
using SmartBank.Client.Forms;
using SmartBank.Client.Security;

namespace SmartBank.Client
{
    internal static class Program
    {
        /// <summary>
        ///  The main entry point for the application.
        /// </summary>
        [STAThread]
        static void Main()
        {
            ApplicationConfiguration.Initialize();

            while (true)
            {
                SessionManager.Instance.LoadSession();

                if (SessionManager.Instance.IsAuthenticated)
                {
                    Form mainForm = SessionManager.Instance.Role.Equals("Admin", StringComparison.OrdinalIgnoreCase)
                        ? new AdminDashboardForm()
                        : new DashboardForm();

                    Application.Run(mainForm);

                    // If user closed form while still authenticated (closed window without clicking Logout)
                    if (SessionManager.Instance.IsAuthenticated)
                    {
                        break;
                    }
                    // If user clicked Logout, IsAuthenticated is false, loop continues and opens LoginForm
                }
                else
                {
                    var loginForm = new LoginForm();
                    Application.Run(loginForm);

                    // If user closed LoginForm without logging in
                    if (!SessionManager.Instance.IsAuthenticated)
                    {
                        break;
                    }
                    // If user logged in, loop continues and opens the appropriate Dashboard
                }
            }
        }
    }
}
