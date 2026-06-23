using System.Globalization;
using System.IO;
using System.Printing;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using SukiMars.Services;

namespace SukiMars.Views
{
    public partial class InventoryPage : Page
    {
        private readonly InventoryService _inventoryService = new();
        private InventoryProduct? _selectedProduct;
        private InventoryProduct? _viewProduct;
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
            return string.Equals(txt, SearchPlaceholder, StringComparison.Ordinal)
                ? string.Empty : txt.Trim();
        }

        private List<InventoryProduct> _allProducts = new();

        private async Task LoadDataAsync(string? search = null)
        {
            try
            {
                _allProducts = await _inventoryService.GetProductsAsync(search ?? GetSearchText());
                var summary = await _inventoryService.GetSummaryAsync();

                TotalProductsText.Text = summary.TotalProducts.ToString();
                LowStockText.Text = summary.LowStock.ToString();
                OutOfStockText.Text = summary.OutOfStock.ToString();
                CategoriesText.Text = summary.Categories.ToString();
                InventoryMessageText.Text = string.Empty;

                PopulateCategoryFilter();
                ApplyFilters();
            }
            catch (Exception ex)
            {
                InventoryMessageText.Text = ex.Message;
            }
        }

        private void PopulateCategoryFilter()
        {
            // Preserve selected category
            string selectedCategory = (FilterCategory.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "All Categories";
            
            FilterCategory.Items.Clear();
            var allItem = new ComboBoxItem { Content = "All Categories" };
            FilterCategory.Items.Add(allItem);

            var categories = _allProducts
                .Select(p => p.ItemCategory)
                .Where(c => !string.IsNullOrWhiteSpace(c))
                .Distinct()
                .OrderBy(c => c)
                .ToList();

            ComboBoxItem? itemToSelect = selectedCategory == "All Categories" ? allItem : null;

            foreach (var cat in categories)
            {
                var cbi = new ComboBoxItem { Content = cat };
                FilterCategory.Items.Add(cbi);
                if (cat == selectedCategory) itemToSelect = cbi;
            }

            FilterCategory.SelectedItem = itemToSelect ?? allItem;
        }

        private void Filter_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            ApplyFilters();
        }

        private void ApplyFilters()
        {
            if (_allProducts == null) return;

            var filtered = _allProducts.AsEnumerable();

            // ROP Filter
            string ropFilter = (FilterROP?.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "All ROP";
            if (ropFilter != "All ROP")
            {
                filtered = filtered.Where(p => p.ReorderLevel == ropFilter);
            }

            // Product Type Filter
            string typeFilter = (FilterProductType?.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "All Types";
            if (typeFilter != "All Types")
            {
                filtered = filtered.Where(p => p.ProductType == typeFilter);
            }

            // Category Filter
            string catFilter = (FilterCategory?.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "All Categories";
            if (catFilter != "All Categories")
            {
                filtered = filtered.Where(p => p.ItemCategory == catFilter);
            }

            if (InventoryGrid != null)
            {
                InventoryGrid.ItemsSource = filtered.ToList();
            }
        }


        private async void Search_Click(object sender, RoutedEventArgs e) =>
            await LoadDataAsync(GetSearchText());

        private async void Refresh_Click(object sender, RoutedEventArgs e)
        {
            _selectedProduct = null;
            SearchBox.Text = SearchPlaceholder;
            SearchBox.Foreground = Brushes.Gray;
            await LoadDataAsync();
        }

        // ── ADD PRODUCT ──────────────────────────────────────────────────────
        private void AddProductModal_Click(object sender, RoutedEventArgs e)
        {
            _selectedProduct = null;
            ModalTitle.Text = "Add Product";
            ClearModalInputs();
            ProductModalOverlay.Visibility = Visibility.Visible;
        }

        // ── EDIT PRODUCT ─────────────────────────────────────────────────────
        private void EditProduct_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button { DataContext: InventoryProduct product })
            {
                _selectedProduct = product;
                ModalTitle.Text = "Update Product";

                ModalNameInput.Text = product.ItemName;
                ModalCodeInput.Text = product.ItemCode;
                ModalCategoryInput.Text = product.ItemCategory;
                ModalProductTypeInput.Text = product.ProductType;
                ModalPriceInput.Text = product.Price.ToString("0.00", CultureInfo.InvariantCulture);
                ModalDescriptionInput.Text = product.ItemDescription;
                ModalShelfLifeInput.Text = product.ShelfLifeDays?.ToString() ?? string.Empty;

                ProductModalOverlay.Visibility = Visibility.Visible;
            }
        }

        // ── VIEW PRODUCT ─────────────────────────────────────────────────────
        private void ViewProduct_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button { DataContext: InventoryProduct product })
            {
                _viewProduct = product;

                // Populate detail labels
                ViewNameText.Text = product.ItemName;
                ViewCodeText.Text = product.ItemCode;
                ViewCategoryText.Text = product.ItemCategory;
                ViewPriceText.Text = $"₱{product.Price:N2}";
                ViewStockText.Text = product.CurrentQty.ToString();
                ViewTypeText.Text = product.ProductType;
                ViewExpiryText.Text = product.ExpirationDateDisplay;
                ViewShelfText.Text = product.ShelfLifeDays.HasValue ? $"{product.ShelfLifeDays} days" : "N/A";
                ViewStatusText.Text = product.Status;
                ViewDescText.Text = product.ItemDescription;

                // Generate barcode from SKU
                GenerateViewBarcode(product.ItemCode);

                ViewModalOverlay.Visibility = Visibility.Visible;
            }
        }

        private void GenerateViewBarcode(string sku)
        {
            if (string.IsNullOrWhiteSpace(sku))
            {
                ViewBarcodeImage.Source = null;
                ViewBarcodeValueText.Text = string.Empty;
                return;
            }
            try
            {
                ViewBarcodeImage.Source = Code128Generator.Generate(sku, 440, 80);
                ViewBarcodeValueText.Text = sku;
            }
            catch
            {
                ViewBarcodeImage.Source = null;
                ViewBarcodeValueText.Text = "(barcode unavailable)";
            }
        }

        private void ViewModalClose_Click(object sender, RoutedEventArgs e)
        {
            ViewModalOverlay.Visibility = Visibility.Collapsed;
            _viewProduct = null;
        }

        private void ViewPrintBarcode_Click(object sender, RoutedEventArgs e)
        {
            string sku = _viewProduct?.ItemCode ?? string.Empty;
            if (string.IsNullOrWhiteSpace(sku)) return;
            PrintBarcodeVisual(sku, _viewProduct?.ItemName ?? sku);
        }

        private void ViewSaveBarcode_Click(object sender, RoutedEventArgs e)
        {
            string sku = _viewProduct?.ItemCode ?? string.Empty;
            if (string.IsNullOrWhiteSpace(sku)) return;
            SaveBarcodePng(sku);
        }

        // ── SAVE (ADD / UPDATE) ───────────────────────────────────────────────
        private async void ModalSave_Click(object sender, RoutedEventArgs e)
        {
            if (!TryGetModalInputs(out string itemName, out string itemCode,
                    out string category, out decimal price,
                    out string description, out string productType, out int? shelfLifeDays))
                return;

            // Barcode is always the SKU
            string barcode = itemCode;

            try
            {
                if (_selectedProduct == null)
                {
                    await _inventoryService.AddProductAsync(itemName, itemCode, category, price, 0, barcode, description, productType, shelfLifeDays);
                    InventoryMessageText.Foreground = Brushes.DarkGreen;
                    InventoryMessageText.Text = "Product added.";
                }
                else
                {
                    int qty = _selectedProduct.CurrentQty;
                    await _inventoryService.UpdateProductAsync(_selectedProduct.ItemId, itemName, itemCode, category, price, qty, barcode, description, productType, shelfLifeDays);
                    InventoryMessageText.Foreground = Brushes.DarkGreen;
                    InventoryMessageText.Text = "Product updated.";
                }

                ProductModalOverlay.Visibility = Visibility.Collapsed;
                ClearModalInputs();
                await LoadDataAsync();
            }
            catch (Exception ex)
            {
                InventoryMessageText.Foreground = Brushes.DarkRed;
                InventoryMessageText.Text = ex.Message;
            }
        }

        private void ModalCancel_Click(object sender, RoutedEventArgs e)
        {
            ProductModalOverlay.Visibility = Visibility.Collapsed;
            ClearModalInputs();
        }

        // ── HELPERS ───────────────────────────────────────────────────────────
        private void ClearModalInputs()
        {
            ModalNameInput.Text = string.Empty;
            ModalCodeInput.Text = string.Empty;
            ModalCategoryInput.Text = string.Empty;
            ModalProductTypeInput.SelectedIndex = 0;
            ModalPriceInput.Text = string.Empty;
            ModalDescriptionInput.Text = string.Empty;
            ModalShelfLifeInput.Text = string.Empty;
        }

        private bool TryGetModalInputs(out string itemName, out string itemCode,
            out string category, out decimal price,
            out string description, out string productType, out int? shelfLifeDays)
        {
            itemName = ModalNameInput.Text.Trim();
            itemCode = ModalCodeInput.Text.Trim();
            category = ModalCategoryInput.Text.Trim();
            productType = ModalProductTypeInput.Text;
            description = ModalDescriptionInput.Text.Trim();
            price = 0m;
            shelfLifeDays = null;

            if (string.IsNullOrWhiteSpace(itemName) || string.IsNullOrWhiteSpace(itemCode))
            {
                MessageBox.Show("Product name and code are required.", "Validation",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }

            if (!decimal.TryParse(ModalPriceInput.Text.Trim(), NumberStyles.Any,
                    CultureInfo.InvariantCulture, out price))
            {
                MessageBox.Show("Invalid price.", "Validation",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }
            
            if (productType == "Perishable")
            {
                if (!string.IsNullOrWhiteSpace(ModalShelfLifeInput.Text) && int.TryParse(ModalShelfLifeInput.Text.Trim(), out int parsedShelf))
                {
                    shelfLifeDays = parsedShelf;
                }
                else
                {
                    MessageBox.Show("Valid Shelf Life (Days) is recommended for perishable goods.", "Information", MessageBoxButton.OK, MessageBoxImage.Information);
                    // Decide if we should return false to block saving, or allow it. I will allow it but they might need it.
                }
            }

            return true;
        }

        // ── BARCODE PRINT / SAVE ──────────────────────────────────────────────
        private void PrintBarcodeVisual(string sku, string label)
        {
            try
            {
                BitmapSource img = Code128Generator.Generate(sku, 500, 100);
                var dlg = new PrintDialog();
                if (dlg.ShowDialog() != true) return;

                var sp = new StackPanel { Width = dlg.PrintableAreaWidth, Margin = new Thickness(40) };
                sp.Children.Add(new TextBlock
                {
                    Text = label,
                    FontSize = 16, FontWeight = FontWeights.SemiBold,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Margin = new Thickness(0, 0, 0, 10)
                });
                sp.Children.Add(new System.Windows.Controls.Image
                {
                    Source = img, Height = 120,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Stretch = Stretch.Uniform
                });
                sp.Children.Add(new TextBlock
                {
                    Text = sku, FontSize = 13,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Margin = new Thickness(0, 6, 0, 0)
                });

                sp.Measure(new System.Windows.Size(dlg.PrintableAreaWidth, dlg.PrintableAreaHeight));
                sp.Arrange(new Rect(new System.Windows.Size(dlg.PrintableAreaWidth, dlg.PrintableAreaHeight)));
                sp.UpdateLayout();
                dlg.PrintVisual(sp, $"Barcode - {sku}");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Print error: {ex.Message}", "Print",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void SaveBarcodePng(string sku)
        {
            try
            {
                var dlg = new SaveFileDialog
                {
                    Title = "Save Barcode",
                    Filter = "PNG Image|*.png",
                    FileName = $"barcode_{sku}.png"
                };
                if (dlg.ShowDialog() != true) return;
                Code128Generator.SaveToPng(sku, dlg.FileName, 500, 100);
                MessageBox.Show($"Barcode saved to:\n{dlg.FileName}", "Saved",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Save error: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // ── SEARCH BOX PLACEHOLDER BEHAVIOUR ─────────────────────────────────
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
            else
            {
                SearchBox.Foreground = Brushes.Black;
            }
        }
    }
}