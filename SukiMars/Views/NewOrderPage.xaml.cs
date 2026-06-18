using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using SukiMars.Services;

namespace SukiMars.Views
{
    public partial class NewOrderPage : Page
    {
        private readonly WarehouseService _warehouseService = new();
        private readonly UserSession? _user;
        private readonly List<NewOrderItem> _currentItems = new();

        public NewOrderPage(UserSession? user = null)
        {
            _user = user;
            InitializeComponent();
            Loaded += NewOrderPage_Loaded;
        }

        private async void NewOrderPage_Loaded(object? sender, RoutedEventArgs e)
        {
            await LoadLookupsAsync();
            await LoadProcessedOrdersAsync();
            EstDeliveryDate.SelectedDate = DateTime.Today.AddDays(7);
        }

        private async Task LoadLookupsAsync()
        {
            var suppliers = await _warehouseService.GetSuppliersAsync();
            SupplierCombo.ItemsSource = suppliers;

            var products = await _warehouseService.GetProductsLookupAsync();
            SkuCombo.ItemsSource = products;
        }

        private async Task LoadProcessedOrdersAsync()
        {
            var processed = await _warehouseService.GetProcessedOrderItemsAsync();
            ProcessedOrdersGrid.ItemsSource = processed;
        }

        private void AddItem_Click(object sender, RoutedEventArgs e)
        {
            if (SkuCombo.SelectedItem is not ProductLookup product)
            {
                MessageBox.Show("Select an item SKU.", "Validation", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (!int.TryParse(QtyInput.Text?.Trim() ?? "0", out int qty) || qty <= 0)
            {
                MessageBox.Show("Enter a valid quantity.", "Validation", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var item = new NewOrderItem
            {
                ItemId = product.ItemId,
                ItemName = product.ItemName,
                ItemCode = product.ItemCode,
                Quantity = qty
            };

            _currentItems.Add(item);
            RefreshCurrentOrder();
            QtyInput.Clear();
            SkuCombo.SelectedIndex = -1;
        }

        private void RemoveItem_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button { Tag: NewOrderItem item })
            {
                _currentItems.Remove(item);
                RefreshCurrentOrder();
            }
        }

        private void ClearItems_Click(object sender, RoutedEventArgs e)
        {
            _currentItems.Clear();
            RefreshCurrentOrder();
        }

        private void RefreshCurrentOrder()
        {
            CurrentOrderGrid.ItemsSource = null;
            CurrentOrderGrid.ItemsSource = _currentItems.ToList();
            int totalQty = _currentItems.Sum(i => i.Quantity);
            ItemCountText.Text = $"{_currentItems.Count} item{(_currentItems.Count == 1 ? "" : "s")} · {totalQty} total qty";
        }

        private async void ConfirmButton_Click(object sender, RoutedEventArgs e)
        {
            if (SupplierCombo.SelectedItem == null)
            {
                MessageBox.Show("Select a supplier.", "Validation", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (!_currentItems.Any())
            {
                MessageBox.Show("Add at least one item to process.", "Validation", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var supplier = SupplierCombo.SelectedItem as Supplier;
            int? supplierId = supplier?.SupplierID;
            DateTime est = EstDeliveryDate.SelectedDate ?? DateTime.Today.AddDays(7);

            var details = _currentItems.Select(i => new WarehouseShipmentDetail { ItemId = i.ItemId, Quantity = i.Quantity }).ToList();

            try
            {
                int createdInventoryId = await _warehouseService.CreateShipmentAsync(supplierId, CommentsBox.Text?.Trim(), est, details, _user?.UserId, _user?.FullName);
                MessageBox.Show("Order processed successfully.", "Success", MessageBoxButton.OK, MessageBoxImage.Information);

                NavigationService?.Navigate(new WarehousePage(_user));
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to process order: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            NavigationService?.Navigate(new WarehousePage(_user));
        }

        private class NewOrderItem
        {
            public int ItemId { get; set; }
            public string ItemName { get; set; } = string.Empty;
            public string ItemCode { get; set; } = string.Empty;
            public int Quantity { get; set; }
        }
    }
}
