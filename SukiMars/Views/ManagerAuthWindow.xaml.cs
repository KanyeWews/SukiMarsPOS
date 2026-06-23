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
            string pin = PincodeInput.Password;

            if (string.IsNullOrWhiteSpace(pin))
            {
                AuthErrorText.Text = "Please enter your PIN code.";
                return;
            }

            try
            {
                bool valid = await _auth.VerifyManagerPinAsync(pin);
                if (!valid)
                {
                    AuthErrorText.Text = "Invalid PIN. Access denied.";
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
