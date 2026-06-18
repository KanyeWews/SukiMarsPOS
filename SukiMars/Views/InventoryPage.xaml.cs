using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using SukiMars.Services;

namespace SukiMars.Views
{
    public partial class InventoryPage : Page
    {
        private readonly InventoryService _inventoryService = new();
        private InventoryProduct? _selectedProduct;
        private const string SearchPlaceholder = "Search product...";

        public InventoryPage()
        {
            InitializeComponent();
            Loaded += InventoryPage_Loaded;
        }

        private async void InventoryPage_Loaded(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(SearchBox.Text))
            {
                SearchBox.Text = SearchPlaceholder;
                SearchBox.Foreground = Brushes.Gray;
            }

            await LoadDataAsync();
        }

        private string GetSearchText()
        {
            var txt = SearchBox.Text ?? string.Empty;
            if (string.Equals(txt, SearchPlaceholder, System.StringComparison.Ordinal))
                return string.Empty;
            return txt.Trim();
        }

        private async Task LoadDataAsync(string? search = null)
        {
            try
            {
                List<InventoryProduct> products = await _inventoryService.GetProductsAsync(search ?? GetSearchText());
                InventorySummary summary = await _inventoryService.GetSummaryAsync();

                InventoryGrid.ItemsSource = products;
                TotalProductsText.Text = summary.TotalProducts.ToString();
                LowStockText.Text = summary.LowStock.ToString();
                OutOfStockText.Text = summary.OutOfStock.ToString();
                CategoriesText.Text = summary.Categories.ToString();
                InventoryMessageText.Text = string.Empty;
            }
            catch (Exception ex)
            {
                InventoryMessageText.Text = ex.Message;
            }
        }

        private async void Search_Click(object sender, RoutedEventArgs e)
        {
            await LoadDataAsync(GetSearchText());
        }

        private async void Refresh_Click(object sender, RoutedEventArgs e)
        {
            SearchBox.Text = string.Empty;
            _selectedProduct = null;
            ClearInputs();

            if (string.IsNullOrWhiteSpace(SearchBox.Text))
            {
                SearchBox.Text = SearchPlaceholder;
                SearchBox.Foreground = Brushes.Gray;
            }

            await LoadDataAsync();
        }

        private async void Add_Click(object sender, RoutedEventArgs e)
        {
            if (!TryGetInputs(out string itemName, out string itemCode, out string barcode, out string category, out decimal price, out int qty))
            {
                return;
            }

            try
            {
                await _inventoryService.AddProductAsync(itemName, itemCode, category, price, 0, barcode);
                InventoryMessageText.Foreground = System.Windows.Media.Brushes.DarkGreen;
                InventoryMessageText.Text = "Product added.";
                ClearInputs();
                await LoadDataAsync();
            }
            catch (Exception ex)
            {
                InventoryMessageText.Foreground = System.Windows.Media.Brushes.DarkRed;
                InventoryMessageText.Text = ex.Message;
            }
        }

        private async void Update_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedProduct is null)
            {
                InventoryMessageText.Text = "Select a product first.";
                return;
            }

            if (!TryGetInputs(out string itemName, out string itemCode, out string barcode, out string category, out decimal price, out int qty))
            {
                return;
            }

            try
            {
                int qtyToPreserve = _selectedProduct.CurrentQty;
                await _inventoryService.UpdateProductAsync(_selectedProduct.ItemId, itemName, itemCode, category, price, qtyToPreserve, barcode);
                InventoryMessageText.Foreground = System.Windows.Media.Brushes.DarkGreen;
                InventoryMessageText.Text = "Product updated.";
                await LoadDataAsync();
            }
            catch (Exception ex)
            {
                InventoryMessageText.Foreground = System.Windows.Media.Brushes.DarkRed;
                InventoryMessageText.Text = ex.Message;
            }
        }

        private async void Delete_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedProduct is null)
            {
                InventoryMessageText.Text = "Select a product to delete.";
                return;
            }

            try
            {
                await _inventoryService.DeleteProductAsync(_selectedProduct.ItemId);
                InventoryMessageText.Foreground = System.Windows.Media.Brushes.DarkGreen;
                InventoryMessageText.Text = "Product deleted.";
                _selectedProduct = null;
                ClearInputs();
                await LoadDataAsync();
            }
            catch (Exception ex)
            {
                InventoryMessageText.Foreground = System.Windows.Media.Brushes.DarkRed;
                InventoryMessageText.Text = $"Delete failed: {ex.Message}";
            }
        }

        private void InventoryGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            _selectedProduct = InventoryGrid.SelectedItem as InventoryProduct;
            if (_selectedProduct is null)
            {
                return;
            }

            NameInput.Text = _selectedProduct.ItemName;
            CodeInput.Text = _selectedProduct.ItemCode;
            BarcodeInput.Text = _selectedProduct.Barcode;
            CategoryInput.Text = _selectedProduct.ItemCategory;
            PriceInput.Text = _selectedProduct.Price.ToString("0.00", CultureInfo.InvariantCulture);
        }

        private bool TryGetInputs(out string itemName, out string itemCode, out string barcode, out string category, out decimal price, out int qty)
        {
            itemName = NameInput.Text.Trim();
            itemCode = CodeInput.Text.Trim();
            barcode = BarcodeInput.Text.Trim();
            category = CategoryInput.Text.Trim();
            price = 0m;
            qty = 0;

            if (string.IsNullOrWhiteSpace(itemName) || string.IsNullOrWhiteSpace(itemCode))
            {
                InventoryMessageText.Text = "Product name and code are required.";
                return false;
            }

            if (!decimal.TryParse(PriceInput.Text.Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out price))
            {
                InventoryMessageText.Text = "Invalid price.";
                return false;
            }

            InventoryMessageText.Text = string.Empty;
            return true;
        }

        private void ClearInputs()
        {
            NameInput.Text = string.Empty;
            CodeInput.Text = string.Empty;
            BarcodeInput.Text = string.Empty;
            CategoryInput.Text = string.Empty;
            PriceInput.Text = string.Empty;
        }

        private void ItemDescription_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedProduct is null)
            {
                InventoryMessageText.Foreground = System.Windows.Media.Brushes.DarkRed;
                InventoryMessageText.Text = "Select a product first.";
                return;
            }

            DescriptionProductName.Text = _selectedProduct.ItemName;
            DescriptionInput.Text = _selectedProduct.ItemDescription;
            DescriptionOverlay.Visibility = Visibility.Visible;
        }

        private async void DescriptionSave_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedProduct is null) return;

            try
            {
                await _inventoryService.UpdateDescriptionAsync(_selectedProduct.ItemId, DescriptionInput.Text.Trim());
                DescriptionOverlay.Visibility = Visibility.Collapsed;
                InventoryMessageText.Foreground = System.Windows.Media.Brushes.DarkGreen;
                InventoryMessageText.Text = "Description updated.";
                await LoadDataAsync();
            }
            catch (Exception ex)
            {
                InventoryMessageText.Foreground = System.Windows.Media.Brushes.DarkRed;
                InventoryMessageText.Text = ex.Message;
            }
        }

        private void DescriptionCancel_Click(object sender, RoutedEventArgs e)
        {
            DescriptionOverlay.Visibility = Visibility.Collapsed;
        }

        private void SearchBox_GotFocus(object sender, RoutedEventArgs e)
        {
            if (string.Equals(SearchBox.Text, SearchPlaceholder, System.StringComparison.Ordinal))
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
            else
            {
                SearchBox.Foreground = Brushes.Black;
            }
        }
    }
}