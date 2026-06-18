using System.Threading.Tasks;
using System.Windows.Controls;
using System.Windows.Input;
using SukiMars.Services;

namespace SukiMars.Views
{
    public partial class SignUpPage : Page
    {
        private readonly AuthService _authService = new();
        private int? _editingUserId;
        private bool _isEditMode;

        // default constructor (create)
        public SignUpPage() : this(null) { }

        // optional editing constructor: pass a userId to enter edit mode
        public SignUpPage(int? editingUserId = null)
        {
            InitializeComponent();

            _editingUserId = editingUserId;
            if (_editingUserId.HasValue)
            {
                _isEditMode = true;
                HeaderText.Text = "Edit Account";
                SubmitButton.Content = "Save";
                UsernameInput.IsEnabled = false; // username is not editable in edit mode
                _ = LoadUserForEditAsync(_editingUserId.Value);
            }
        }

        private async Task LoadUserForEditAsync(int userId)
        {
            SignUpMessageText.Text = string.Empty;
            try
            {
                var details = await _authService.GetUserByIdAsync(userId);
                if (details == null)
                {
                    SignUpMessageText.Text = "User not found.";
                    return;
                }

                FirstNameInput.Text = details.FirstName;
                LastNameInput.Text = details.LastName;
                UsernameInput.Text = details.Username;
                if (!string.IsNullOrEmpty(details.Pincode))
                {
                    PincodeInput.Text = details.Pincode;
                }

                // set role radios
                if (details.Role.Equals("Manager", System.StringComparison.OrdinalIgnoreCase))
                {
                    ManagerRoleRadio.IsChecked = true;
                }
                else if (details.Role.Equals("Admin", System.StringComparison.OrdinalIgnoreCase))
                {
                    AdminRoleRadio.IsChecked = true;
                }
                else
                {
                    CashierRoleRadio.IsChecked = true;
                }
            }
            catch (System.Exception ex)
            {
                SignUpMessageText.Text = ex.Message;
            }
        }

        private async void CreateAccount_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            SignUpMessageText.Text = string.Empty;

            string firstName = FirstNameInput.Text.Trim();
            string lastName = LastNameInput.Text.Trim();
            string username = UsernameInput.Text.Trim();
            string password = PasswordInput.Password;
            string confirmPassword = ConfirmPasswordInput.Password;
            string pincode = PincodeInput.Text.Trim();

            if (string.IsNullOrWhiteSpace(username))
            {
                SignUpMessageText.Text = "Username is required.";
                return;
            }

            // if user is switching password during edit, verify match; for create always required
            if (!_isEditMode || !string.IsNullOrEmpty(password) || !string.IsNullOrEmpty(confirmPassword))
            {
                if (!password.Equals(confirmPassword, System.StringComparison.Ordinal))
                {
                    SignUpMessageText.Text = "Passwords do not match.";
                    return;
                }
            }

            // pincode validation: optional, but if provided must be digits and up to 4 chars
            if (!string.IsNullOrEmpty(pincode))
            {
                if (pincode.Length > 4 || !int.TryParse(pincode, out _))
                {
                    SignUpMessageText.Text = "Pincode must be up to 4 digits.";
                    return;
                }
            }

            string role = CashierRoleRadio.IsChecked == true
                ? "Cashier"
                : ManagerRoleRadio.IsChecked == true
                    ? "Manager"
                    : "Admin";

            try
            {
                if (_isEditMode && _editingUserId.HasValue)
                {
                    // if password fields are left empty, do not change password
                    string? newPassword = string.IsNullOrEmpty(password) ? null : password;
                    await _authService.UpdateUserAsync(_editingUserId.Value, firstName, lastName, role, pincode, newPassword);

                    SignUpMessageText.Foreground = System.Windows.Media.Brushes.DarkGreen;
                    SignUpMessageText.Text = "Account updated successfully.";
                }
                else
                {
                    // create new account
                    if (string.IsNullOrWhiteSpace(password))
                    {
                        SignUpMessageText.Text = "Password is required.";
                        return;
                    }

                    await _authService.CreateAccountAsync(username, password, role, firstName, lastName, pincode);

                    SignUpMessageText.Foreground = System.Windows.Media.Brushes.DarkGreen;
                    SignUpMessageText.Text = "Account created successfully. You can now log in.";

                    FirstNameInput.Clear();
                    LastNameInput.Clear();
                    UsernameInput.Clear();
                    PasswordInput.Clear();
                    ConfirmPasswordInput.Clear();
                    PincodeInput.Clear();
                    CashierRoleRadio.IsChecked = true;
                }
            }
            catch (System.Exception ex)
            {
                SignUpMessageText.Foreground = System.Windows.Media.Brushes.DarkRed;
                SignUpMessageText.Text = ex.Message;
            }
        }

        private void UsernameInput_TextChanged(object sender, TextChangedEventArgs e)
        {

        }

        private void PincodeInput_PreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            // allow only digits
            e.Handled = !char.IsDigit(e.Text, 0);
        }
    }
}
