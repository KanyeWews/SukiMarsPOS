using System;
using System.Threading.Tasks;
using System.Windows;
using SukiMars.Services;

namespace SukiMars.Views
{
    public partial class ManagerAuthWindow : Window
    {
        private readonly AuthService _auth = new();

        public ManagerAuthWindow()
        {
            InitializeComponent();
        }

        public bool IsAuthorized { get; private set; } = false;

        private async void Authorize_Click(object sender, RoutedEventArgs e)
        {
            AuthErrorText.Text = string.Empty;
            string user = UsernameInput.Text?.Trim() ?? string.Empty;
            string pass = PasswordInput.Password;
            string pin = PincodeInput.Text?.Trim() ?? string.Empty;

            try
            {
                var acct = await _auth.AuthenticateAsync(user, pass);
                if (acct is null)
                {
                    AuthErrorText.Text = "Invalid username or password.";
                    return;
                }

                // require manager role ONLY (deny Admin and Cashier)
                if (!acct.Role.Equals("Manager", StringComparison.OrdinalIgnoreCase))
                {
                    AuthErrorText.Text = "Manager credentials required.";
                    return;
                }

                // verify pincode if present in DB
                var details = await _auth.GetUserByIdAsync(acct.UserId);
                if (details is null || string.IsNullOrWhiteSpace(details.Pincode) || details.Pincode != pin)
                {
                    AuthErrorText.Text = "Invalid pincode.";
                    return;
                }

                IsAuthorized = true;
                DialogResult = true;
                Close();
            }
            catch (Exception ex)
            {
                AuthErrorText.Text = ex.Message;
            }
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            IsAuthorized = false;
            DialogResult = false;
            Close();
        }
    }
}
