using System.Windows.Controls;
using SukiMars.Services;

namespace SukiMars.Views
{
    public partial class SignUpPage : Page
    {
        private readonly AuthService _authService = new();

        public SignUpPage()
        {
            InitializeComponent();
        }

        private async void CreateAccount_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            SignUpMessageText.Text = string.Empty;

            string username = UsernameInput.Text.Trim();
            string password = PasswordInput.Password;
            string confirmPassword = ConfirmPasswordInput.Password;

            if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
            {
                SignUpMessageText.Text = "Username and password are required.";
                return;
            }

            if (!password.Equals(confirmPassword, StringComparison.Ordinal))
            {
                SignUpMessageText.Text = "Passwords do not match.";
                return;
            }

            string role = CashierRoleRadio.IsChecked == true
                ? "Cashier"
                : ManagerRoleRadio.IsChecked == true
                    ? "Manager"
                    : "Admin";

            try
            {
                await _authService.CreateAccountAsync(username, password, role);
                SignUpMessageText.Foreground = System.Windows.Media.Brushes.DarkGreen;
                SignUpMessageText.Text = "Account created successfully. You can now log in.";
                UsernameInput.Clear();
                PasswordInput.Clear();
                ConfirmPasswordInput.Clear();
                CashierRoleRadio.IsChecked = true;
            }
            catch (Exception ex)
            {
                SignUpMessageText.Foreground = System.Windows.Media.Brushes.DarkRed;
                SignUpMessageText.Text = ex.Message;
            }
        }
    }
}
