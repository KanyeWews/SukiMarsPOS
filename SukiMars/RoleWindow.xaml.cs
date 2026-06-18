using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using SukiMars.Services;
using SukiMars.Views;

namespace SukiMars;

public partial class RoleWindow : Window
{
    private readonly UserSession _user;
    // session timeout timer
    private readonly DispatcherTimer _sessionTimer;
    private static readonly TimeSpan SessionTimeout = TimeSpan.FromMinutes(30);

    public RoleWindow(UserSession user)
    {
        _user = user;
        InitializeComponent();
        ConfigureRoleAccess();
        NavigateToDefaultPage();
        // Setup session timeout timer
        _sessionTimer = new DispatcherTimer(DispatcherPriority.ApplicationIdle)
        {
            Interval = SessionTimeout
        };
        _sessionTimer.Tick += SessionTimer_Tick;
        _sessionTimer.Start();

        // Reset timer on user input anywhere in the window
        this.PreviewMouseMove += ResetSessionTimerOnInput;
        this.PreviewKeyDown += ResetSessionTimerOnInput;
        this.PreviewTouchDown += ResetSessionTimerOnInput;
        this.PreviewStylusDown += ResetSessionTimerOnInput;
    }

    private void ResetSessionTimerOnInput(object? sender, InputEventArgs e)
    {
        if (_sessionTimer.IsEnabled)
        {
            _sessionTimer.Stop();
            _sessionTimer.Start();
        }
    }

    private void SessionTimer_Tick(object? sender, EventArgs e)
    {
        // time elapsed without activity - force logout
        _sessionTimer.Stop();
        MessageBox.Show("You have been logged out due to inactivity.", "Session Timeout", MessageBoxButton.OK, MessageBoxImage.Information);
        LogOut();
    }

    private void ConfigureRoleAccess()
    {
        CurrentUserText.Text = $"{_user.Username} ({_user.Role})";

        string role = _user.Role.Trim();
        bool isCashier = role.Equals("Cashier", StringComparison.OrdinalIgnoreCase);
        bool isManager = role.Equals("Manager", StringComparison.OrdinalIgnoreCase);
        bool isAdmin = role.Equals("Admin", StringComparison.OrdinalIgnoreCase);

        CashierButton.Visibility = (isCashier || isManager || isAdmin) ? Visibility.Visible : Visibility.Collapsed;
        InventoryButton.Visibility = (isManager || isAdmin) ? Visibility.Visible : Visibility.Collapsed;
        ReportsButton.Visibility = (isManager || isAdmin) ? Visibility.Visible : Visibility.Collapsed;
        WarehouseButton.Visibility = (isManager || isAdmin) ? Visibility.Visible : Visibility.Collapsed;
        UsersButton.Visibility = isAdmin ? Visibility.Visible : Visibility.Collapsed;
    }

    private void NavigateToDefaultPage()
    {
        string role = _user.Role.Trim();
        if (role.Equals("Cashier", StringComparison.OrdinalIgnoreCase))
        {
            ContentFrame.Navigate(new CashierPosPage(_user));
            return;
        }

        if (role.Equals("Manager", StringComparison.OrdinalIgnoreCase))
        {
            ContentFrame.Navigate(new InventoryPage());
            return;
        }

        if (role.Equals("Admin", StringComparison.OrdinalIgnoreCase))
        {
            ContentFrame.Navigate(new AdminUsersPage());
            return;
        }

        MessageBox.Show($"Unknown role '{_user.Role}'.", "Role Error", MessageBoxButton.OK, MessageBoxImage.Warning);
        LogOut();
    }

    private void CashierButton_Click(object sender, RoutedEventArgs e) => ContentFrame.Navigate(new CashierPosPage(_user));
    private void DashboardButton_Click(object sender, RoutedEventArgs e) => NavigateToDefaultPage();
    private void InventoryButton_Click(object sender, RoutedEventArgs e) => ContentFrame.Navigate(new InventoryPage());
    private void ReportsButton_Click(object sender, RoutedEventArgs e) => ContentFrame.Navigate(new ReportsPage());
    private void WarehouseButton_Click(object sender, RoutedEventArgs e) => ContentFrame.Navigate(new WarehousePage(_user));
    private void UsersButton_Click(object sender, RoutedEventArgs e) => ContentFrame.Navigate(new AdminUsersPage());
    private void LogoutButton_Click(object sender, RoutedEventArgs e) => LogOut();

    private void LogOut()
    {
        // stop timer and navigate to login
        try
        {
            _sessionTimer?.Stop();
        }
        catch { }

        MainWindow loginWindow = new();
        loginWindow.Show();
        Close();
    }
}
