using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using SukiMars.Services;
using SukiMars; // for RoleWindow

namespace SukiMars.Views
{
    public partial class LoginPage : Page
    {
        private const string UsernamePlaceholder = "Enter Username";
        private readonly AuthService _authService = new();

        public LoginPage()
        {
            InitializeComponent();

            if (string.IsNullOrWhiteSpace(UsernameTextBox.Text))
            {
                UsernameTextBox.Text = UsernamePlaceholder;
                UsernameTextBox.Foreground = Brushes.Gray;
            }

            PasswordPlaceholder.Visibility = string.IsNullOrEmpty(PasswordInput.Password) ? Visibility.Visible : Visibility.Collapsed;
        }

        private void Username_GotFocus(object sender, RoutedEventArgs e)
        {
            if (string.Equals(UsernameTextBox.Text, UsernamePlaceholder))
            {
                UsernameTextBox.Text = string.Empty;
                UsernameTextBox.Foreground = Brushes.Black;
            }
        }

        private void Username_LostFocus(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(UsernameTextBox.Text))
            {
                UsernameTextBox.Text = UsernamePlaceholder;
                UsernameTextBox.Foreground = Brushes.Gray;
            }
            else
            {
                UsernameTextBox.Foreground = Brushes.Black;
            }
        }

        private void PasswordInput_PasswordChanged(object sender, RoutedEventArgs e)
        {
            PasswordPlaceholder.Visibility = string.IsNullOrEmpty(PasswordInput.Password) ? Visibility.Visible : Visibility.Collapsed;
        }

        private void PasswordInput_GotFocus(object sender, RoutedEventArgs e)
        {
            PasswordPlaceholder.Visibility = Visibility.Collapsed;
        }

        private void PasswordInput_LostFocus(object sender, RoutedEventArgs e)
        {
            PasswordPlaceholder.Visibility = string.IsNullOrEmpty(PasswordInput.Password) ? Visibility.Visible : Visibility.Collapsed;
        }

        private async void SignIn_Click(object sender, RoutedEventArgs e)
        {
            LoginMessageText.Text = string.Empty;

            var username = UsernameTextBox.Text?.Trim();
            if (string.Equals(username, UsernamePlaceholder, StringComparison.Ordinal))
            {
                username = string.Empty;
            }

            var password = PasswordInput.Password;

            if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
            {
                LoginMessageText.Text = "Enter username and password.";
                return;
            }

            try
            {
                var user = await _authService.AuthenticateAsync(username, password);
                if (user is null)
                {
                    LoginMessageText.Text = "The username or password you entered is incorrect.";
                    return;
                }

                if (!string.Equals(user.Status, "Active", StringComparison.OrdinalIgnoreCase))
                {
                    LoginMessageText.Text = "Your account is not active.";
                    return;
                }

                var roleWindow = new RoleWindow(user);
                roleWindow.Show();

                Window.GetWindow(this)?.Close();
            }
            catch (Exception ex)
            {
                LoginMessageText.Text = $"Login failed: {ex.Message}";
            }
        }
    }
}
