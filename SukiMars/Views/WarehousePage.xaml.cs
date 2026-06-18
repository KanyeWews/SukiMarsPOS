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
    public partial class WarehousePage : Page
    {
        private readonly WarehouseService _warehouseService = new();
        private readonly UserSession? _user;
        private List<WarehouseShipment> _allShipments = new();
        private string? _activeStatusChip;
        private WarehouseShipment? _editingShipment;
        private const string SearchPlaceholder = "Search ASN or supplier...";

        public WarehousePage(UserSession? user = null)
        {
            _user = user;
            InitializeComponent();
            Loaded += WarehousePage_Loaded;
        }

        private async void WarehousePage_Loaded(object? sender, RoutedEventArgs e)
        {
            SearchBox.Text = SearchPlaceholder;
            SearchBox.Foreground = Brushes.Gray;

            StatusFilter.ItemsSource = new[] { "All Status", "Pending", "Ordered", "Delivered", "Cancelled" };
            StatusFilter.SelectedIndex = 0;

            DeliveryDateFilter.SelectedDate = null;

            await LoadDataAsync();
        }

        private async Task LoadDataAsync()
        {
            try
            {
                _allShipments = await _warehouseService.GetShipmentsAsync();
                await PopulateSupplierFilterAsync();
                ApplyFilters();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Unable to load shipments: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async Task PopulateSupplierFilterAsync()
        {
            try
            {
                var suppliers = await _warehouseService.GetSuppliersAsync();
                var names = suppliers
                    .Select(s => s.SupplierName)
                    .Where(name => !string.IsNullOrWhiteSpace(name))
                    .ToList();

                names.Insert(0, "All Suppliers");
                SupplierFilter.ItemsSource = names;
                SupplierFilter.SelectedIndex = 0;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Unable to load suppliers: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ApplyFilters()
        {
            IEnumerable<WarehouseShipment> filtered = _allShipments;

            var search = GetSearchText();
            if (!string.IsNullOrWhiteSpace(search))
            {
                filtered = filtered.Where(s =>
                    s.ASNNumber.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                    s.Supplier.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                    (s.Comments?.Contains(search, StringComparison.OrdinalIgnoreCase) ?? false));
            }

            var status = StatusFilter.SelectedItem as string;
            if (!string.IsNullOrWhiteSpace(status) && status != "All Status")
            {
                filtered = filtered.Where(s => s.Status.Equals(status, StringComparison.OrdinalIgnoreCase));
            }

            var supplier = SupplierFilter.SelectedItem as string;
            if (!string.IsNullOrWhiteSpace(supplier) && supplier != "All Suppliers")
            {
                filtered = filtered.Where(s => s.Supplier.Equals(supplier, StringComparison.OrdinalIgnoreCase));
            }

            if (DeliveryDateFilter.SelectedDate is DateTime selectedDate)
            {
                filtered = filtered.Where(s => s.EstimatedDeliveryDate.Date == selectedDate.Date);
            }

            if (!string.IsNullOrWhiteSpace(_activeStatusChip) && _activeStatusChip != "All")
            {
                filtered = filtered.Where(s => s.Status.Equals(_activeStatusChip, StringComparison.OrdinalIgnoreCase));
            }

            var results = filtered.ToList();
            ShipmentsGrid.ItemsSource = results;
            UpdateStatusChips(results);
        }

        private void UpdateStatusChips(IReadOnlyList<WarehouseShipment> visible)
        {
            int Count(string status) => _allShipments.Count(s => s.Status.Equals(status, StringComparison.OrdinalIgnoreCase));

            AllStatusChip.Content = $"All ({_allShipments.Count})";
            PendingChip.Content = $"Pending ({Count("Pending")})";
            OrderedChip.Content = $"Ordered ({Count("Ordered")})";
            DeliveredChip.Content = $"Delivered ({Count("Delivered")})";
            CancelledChip.Content = $"Cancelled ({Count("Cancelled")})";

            SetChipSelected(AllStatusChip, _activeStatusChip is null or "All");
            SetChipSelected(PendingChip, _activeStatusChip == "Pending");
            SetChipSelected(OrderedChip, _activeStatusChip == "Ordered");
            SetChipSelected(DeliveredChip, _activeStatusChip == "Delivered");
            SetChipSelected(CancelledChip, _activeStatusChip == "Cancelled");

            _ = visible;
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
                "Pending" => new SolidColorBrush(Color.FromRgb(0xB4, 0x53, 0x09)),
                "Ordered" => new SolidColorBrush(Color.FromRgb(0x1D, 0x4E, 0xD8)),
                "Delivered" => new SolidColorBrush(Color.FromRgb(0x6D, 0x28, 0xD9)),
                "Cancelled" => new SolidColorBrush(Color.FromRgb(0xB9, 0x1C, 0x1C)),
                _ => Brushes.Transparent
            };
        }

        private string GetSearchText()
        {
            var text = SearchBox.Text ?? string.Empty;
            if (string.Equals(text, SearchPlaceholder, StringComparison.Ordinal))
                return string.Empty;
            return text.Trim();
        }

        private void SearchBox_GotFocus(object sender, RoutedEventArgs e)
        {
            if (string.Equals(SearchBox.Text, SearchPlaceholder, StringComparison.Ordinal))
            {
                SearchBox.Text = string.Empty;
                SearchBox.Foreground = Brushes.Black;
            }
        }

        private void SearchBox_LostFocus(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(SearchBox.Text))
            {
                SearchBox.Text = SearchPlaceholder;
                SearchBox.Foreground = Brushes.Gray;
            }
        }

        private void Filter_Changed(object sender, RoutedEventArgs e)
        {
            if (!IsLoaded)
                return;

            ApplyFilters();
        }

        // Status chips are non-interactive now (IsHitTestVisible="False" in XAML)

        private void NewOrderButton_Click(object sender, RoutedEventArgs e)
        {
            NavigationService?.Navigate(new NewOrderPage(_user));
        }

        private void View_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button { DataContext: WarehouseShipment shipment })
            {
                MessageBox.Show(
                    $"ASN: {shipment.ASNNumber}\nSupplier: {shipment.Supplier}\nStatus: {shipment.Status}\nEst. Delivery: {shipment.EstimatedDeliveryDate:MMM d, yyyy}",
                    "View Shipment",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
        }

        private void Edit_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button { DataContext: WarehouseShipment shipment })
            {
                _editingShipment = shipment;
                StatusOverlayASN.Text = $"ASN: {shipment.ASNNumber}";
                StatusOverlayCurrent.Text = $"Current Status: {shipment.Status}";
                StatusOverlayCombo.ItemsSource = new[] { "Pending", "Ordered", "Delivered", "Cancelled" };
                StatusOverlayCombo.SelectedItem = shipment.Status;
                StatusOverlay.Visibility = Visibility.Visible;
            }
        }

        private async void StatusSave_Click(object sender, RoutedEventArgs e)
        {
            if (_editingShipment is null || StatusOverlayCombo.SelectedItem is not string newStatus)
                return;

            try
            {
                string updatedBy = _user?.FullName ?? "system";

                if (newStatus.Equals("Delivered", StringComparison.OrdinalIgnoreCase))
                {
                    await _warehouseService.ReceiveShipmentAsync(_editingShipment.InventoryId, updatedBy);
                }
                else
                {
                    await _warehouseService.UpdateShipmentStatusAsync(_editingShipment.InventoryId, newStatus, updatedBy);
                }

                StatusOverlay.Visibility = Visibility.Collapsed;
                _editingShipment = null;
                await LoadDataAsync();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to update status: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void StatusCancel_Click(object sender, RoutedEventArgs e)
        {
            StatusOverlay.Visibility = Visibility.Collapsed;
            _editingShipment = null;
        }
    }
}
