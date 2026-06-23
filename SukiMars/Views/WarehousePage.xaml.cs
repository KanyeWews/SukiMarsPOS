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
        private List<ShipmentBatchItem> _receivingItems = new();
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

            StatusFilter.ItemsSource = new[] { "All Status", "Pending", "Ordered", "Received", "Cancelled" };
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
            ReceivedChip.Content = $"Received ({Count("Received")})";
            CancelledChip.Content = $"Cancelled ({Count("Cancelled")})";

            SetChipSelected(AllStatusChip, _activeStatusChip is null or "All");
            SetChipSelected(PendingChip, _activeStatusChip == "Pending");
            SetChipSelected(OrderedChip, _activeStatusChip == "Ordered");
            SetChipSelected(ReceivedChip, _activeStatusChip == "Received");
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
                "Received" => new SolidColorBrush(Color.FromRgb(0x6D, 0x28, 0xD9)),
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

        private async void View_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button { DataContext: WarehouseShipment shipment })
            {
                try
                {
                    var items = await _warehouseService.GetShipmentItemsAsync(shipment.InventoryId);
                    ViewBatchASN.Text = $"ASN: {shipment.ASNNumber} · Supplier: {shipment.Supplier} · Status: {shipment.Status}";
                    ViewBatchGrid.ItemsSource = items;
                    ViewBatchOverlay.Visibility = Visibility.Visible;
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Unable to load items: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void ViewBatchClose_Click(object sender, RoutedEventArgs e)
        {
            ViewBatchOverlay.Visibility = Visibility.Collapsed;
        }

        private void Edit_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button { DataContext: WarehouseShipment shipment })
            {
                _editingShipment = shipment;
                StatusOverlayASN.Text = $"ASN: {shipment.ASNNumber}";
                StatusOverlayCurrent.Text = $"Current Status: {shipment.Status}";
                StatusOverlayCombo.ItemsSource = new[] { "Pending", "Ordered", "Received", "Cancelled" };
                StatusOverlayCombo.SelectedItem = shipment.Status;
                StatusOverlay.Visibility = Visibility.Visible;
            }
        }

        private async void StatusSave_Click(object sender, RoutedEventArgs e)
        {
            if (_editingShipment is null || StatusOverlayCombo.SelectedItem is not string newStatus)
                return;

            // If Received, open the expiration capture modal instead
            if (newStatus.Equals("Received", StringComparison.OrdinalIgnoreCase))
            {
                StatusOverlay.Visibility = Visibility.Collapsed;
                await OpenReceiveModalAsync(_editingShipment);
                return;
            }

            try
            {
                string updatedBy = _user?.FullName ?? "system";
                await _warehouseService.UpdateShipmentStatusAsync(_editingShipment.InventoryId, newStatus, updatedBy);
                StatusOverlay.Visibility = Visibility.Collapsed;
                _editingShipment = null;
                await LoadDataAsync();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to update status: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async Task OpenReceiveModalAsync(WarehouseShipment shipment)
        {
            try
            {
                _receivingItems = await _warehouseService.GetShipmentItemsAsync(shipment.InventoryId);
                ReceiveOverlayASN.Text = $"ASN: {shipment.ASNNumber} · Supplier: {shipment.Supplier}";
                ReceiveBatchList.ItemsSource = _receivingItems;
                ReceiveOverlay.Visibility = Visibility.Visible;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Unable to load items: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void ReceiveConfirm_Click(object sender, RoutedEventArgs e)
        {
            if (_editingShipment is null) return;

            var batchDetails = new List<ReceiveBatchDetail>();

            foreach (var item in _receivingItems)
            {
                // Find the Received Qty TextBox tagged with this item's InventoryDetailId
                int occurrence = 0;
                var recvBox = FindTaggedControlInternal<TextBox>(ReceiveBatchList, item.InventoryDetailId, ref occurrence, 0);

                if (!int.TryParse(recvBox?.Text?.Trim(), out int receivedQty) || receivedQty < 0)
                {
                    MessageBox.Show($"Please enter a valid Received Qty for '{item.ItemName}'.", "Validation",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                DateTime? expDate = null;
                if (item.IsPerishable)
                {
                    int dpOccurrence = 0;
                    var expPicker = FindTaggedControlInternal<DatePicker>(ReceiveBatchList, item.InventoryDetailId, ref dpOccurrence, 0);
                    if (expPicker?.SelectedDate == null)
                    {
                        MessageBox.Show($"Please enter an Expiration Date for '{item.ItemName}'.", "Validation",
                            MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }
                    expDate = expPicker.SelectedDate;
                }

                batchDetails.Add(new ReceiveBatchDetail
                {
                    InventoryDetailId = item.InventoryDetailId,
                    ReceivedQuantity  = receivedQty,
                    ExpirationDate    = expDate
                });
            }

            try
            {
                string updatedBy = _user?.FullName ?? "system";
                await _warehouseService.ReceiveShipmentAsync(_editingShipment.InventoryId, updatedBy, batchDetails);
                ReceiveOverlay.Visibility = Visibility.Collapsed;
                _editingShipment = null;
                _receivingItems.Clear();
                await LoadDataAsync();
                MessageBox.Show("Delivery confirmed and stock updated.", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to receive shipment: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ReceiveCancel_Click(object sender, RoutedEventArgs e)
        {
            ReceiveOverlay.Visibility = Visibility.Collapsed;
            _editingShipment = null;
            _receivingItems.Clear();
        }

        // Helper: find a tagged control of type T within an ItemsControl
        private static T? FindTaggedControl<T>(DependencyObject parent, int tag, int occurrence) where T : FrameworkElement
        {
            int found = 0;
            return FindTaggedControlInternal<T>(parent, tag, ref found, occurrence);
        }

        private static T? FindTaggedControlInternal<T>(DependencyObject parent, int tag, ref int found, int occurrence) where T : FrameworkElement
        {
            for (int i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = System.Windows.Media.VisualTreeHelper.GetChild(parent, i);
                if (child is T ctrl && ctrl.Tag is int t && t == tag)
                {
                    if (found == occurrence) return ctrl;
                    found++;
                }
                var result = FindTaggedControlInternal<T>(child, tag, ref found, occurrence);
                if (result != null) return result;
            }
            return null;
        }

        private static T? GetDescendant<T>(DependencyObject parent) where T : DependencyObject
        {
            for (int i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = System.Windows.Media.VisualTreeHelper.GetChild(parent, i);
                if (child is T t) return t;
                var result = GetDescendant<T>(child);
                if (result != null) return result;
            }
            return null;
        }

        private void StatusCancel_Click(object sender, RoutedEventArgs e)
        {
            StatusOverlay.Visibility = Visibility.Collapsed;
            _editingShipment = null;
        }
    }
}
