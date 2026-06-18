using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using SukiMars.Services;

namespace SukiMars.Views
{
    public partial class AdminUsersPage : Page
    {
        private readonly AuthService _authService = new();
        private List<UserViewModel> _allUsers = new();
        private string? _activeStatusChip;
        private const string SearchPlaceholder = "Search user...";

        public AdminUsersPage()
        {
            InitializeComponent();
            Loaded += AdminUsersPage_Loaded;
        }

        private async void AdminUsersPage_Loaded(object? sender, RoutedEventArgs e)
        {
            SearchUserBox.Text = SearchPlaceholder;
            SearchUserBox.Foreground = Brushes.Gray;
            await LoadUsersAsync();
        }

        private async Task LoadUsersAsync()
        {
            try
            {
                var users = await _authService.GetAllUsersAsync();

                _allUsers = users.Select(u => new UserViewModel
                {
                    UserId = u.UserId,
                    Name = $"{u.FirstName} {u.LastName}".Trim(),
                    Username = u.Username,
                    Role = u.Role,
                    Status = u.Status,
                    ActionLabel = u.Status.Equals("Active", StringComparison.OrdinalIgnoreCase) ? "Deactivate" : "Activate"
                }).ToList();

                ApplyFilters();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to load users: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ApplyFilters()
        {
            IEnumerable<UserViewModel> filtered = _allUsers;

            var search = GetSearchText();
            if (!string.IsNullOrWhiteSpace(search))
            {
                filtered = filtered.Where(u =>
                    u.Name.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                    u.Username.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                    u.Role.Contains(search, StringComparison.OrdinalIgnoreCase));
            }

            if (!string.IsNullOrWhiteSpace(_activeStatusChip) && _activeStatusChip != "All")
            {
                filtered = filtered.Where(u =>
                    u.Status.Equals(_activeStatusChip, StringComparison.OrdinalIgnoreCase));
            }

            var results = filtered.ToList();
            UsersGrid.ItemsSource = results;
            UpdateStatusChips();
        }

        private void UpdateStatusChips()
        {
            int activeCount = _allUsers.Count(u => u.Status.Equals("Active", StringComparison.OrdinalIgnoreCase));
            int inactiveCount = _allUsers.Count(u => !u.Status.Equals("Active", StringComparison.OrdinalIgnoreCase));

            AllStatusChip.Content = $"All ({_allUsers.Count})";
            ActiveStatusChip.Content = $"Active ({activeCount})";
            InactiveStatusChip.Content = $"Inactive ({inactiveCount})";

            SetChipSelected(AllStatusChip, _activeStatusChip is null or "All");
            SetChipSelected(ActiveStatusChip, _activeStatusChip == "Active");
            SetChipSelected(InactiveStatusChip, _activeStatusChip == "Inactive");
        }

        private static void SetChipSelected(Button chip, bool selected)
        {
            chip.Opacity = selected ? 1.0 : 0.72;
            chip.FontWeight = selected ? FontWeights.Bold : FontWeights.SemiBold;

            if (chip.Tag is not string tag || tag == "All")
                return;

            chip.BorderThickness = selected ? new Thickness(2) : new Thickness(0);
            chip.BorderBrush = tag switch
            {
                "Active" => new SolidColorBrush(Color.FromRgb(0x04, 0x78, 0x57)),
                "Inactive" => new SolidColorBrush(Color.FromRgb(0xB9, 0x1C, 0x1C)),
                _ => Brushes.Transparent
            };
        }

        private string GetSearchText()
        {
            var text = SearchUserBox.Text ?? string.Empty;
            if (string.Equals(text, SearchPlaceholder, StringComparison.Ordinal))
                return string.Empty;
            return text.Trim();
        }

        private void SearchUserBox_GotFocus(object sender, RoutedEventArgs e)
        {
            if (string.Equals(SearchUserBox.Text, SearchPlaceholder, StringComparison.Ordinal))
            {
                SearchUserBox.Text = string.Empty;
                SearchUserBox.Foreground = Brushes.Black;
            }
        }

        private void SearchUserBox_LostFocus(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(SearchUserBox.Text))
            {
                SearchUserBox.Text = SearchPlaceholder;
                SearchUserBox.Foreground = Brushes.Gray;
            }
        }

        private void SearchUserBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (!IsLoaded)
                return;

            ApplyFilters();
        }

        private void StatusChip_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button chip || chip.Tag is not string tag)
                return;

            _activeStatusChip = tag == "All" ? null : tag;
            ApplyFilters();
        }

        private async void ToggleStatus_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button { DataContext: UserViewModel vm })
            {
                string newStatus = vm.Status.Equals("Active", StringComparison.OrdinalIgnoreCase) ? "Inactive" : "Active";
                try
                {
                    await _authService.UpdateUserStatusAsync(vm.UserId, newStatus);
                    await LoadUsersAsync();
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Failed to update status: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void AddUser_Click(object sender, RoutedEventArgs e)
        {
            NavigationService?.Navigate(new SignUpPage());
        }

        private void EditUser_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button { DataContext: UserViewModel vm })
            {
                NavigationService?.Navigate(new SignUpPage(vm.UserId));
            }
        }

        private sealed class UserViewModel
        {
            public int UserId { get; set; }
            public string Name { get; set; } = string.Empty;
            public string Username { get; set; } = string.Empty;
            public string Role { get; set; } = string.Empty;
            public string Status { get; set; } = string.Empty;
            public string ActionLabel { get; set; } = "Deactivate";
        }
    }
}
