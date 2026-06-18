using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using SukiMars.Services;
using SukiMars.Views; // for ReceiptWindow

namespace SukiMars.Views
{
    public partial class CashierPosPage : Page
    {
        private readonly UserSession _user;
        private readonly PosService _pos_service = new();
        private readonly List<PosCartItem> _cart = new();
        private readonly List<string> _allCategories = new();
        private readonly List<string> _selectedCategories = new();
        private const string SearchPlaceholder = "Search product...";
        private const string CategoryPlaceholder = "Filter by category...";
        private string _selectedPaymentMethod = "Cash";
        private bool _suppressCategorySelectionChanged;

        public CashierPosPage(UserSession user)
        {
            _user = user;
            InitializeComponent();
            Loaded += CashierPosPage_Loaded;
        }

        private async void CashierPosPage_Loaded(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(SearchBox.Text))
            {
                SearchBox.Text = SearchPlaceholder;
                SearchBox.Foreground = Brushes.Gray;
            }

            SelectedPaymentText.Text = $"({_selectedPaymentMethod})";
            UpdatePaymentUi();

            await LoadCategoriesAsync();
            await LoadProductsAsync();
            UpdateCartUi();
        }

        private async Task LoadCategoriesAsync()
        {
            try
            {
                var cats = await _pos_service.GetCategoriesAsync();
                _allCategories.Clear();
                _allCategories.AddRange(cats);
                RefreshCategoryDropdown();
            }
            catch { }
        }

        private void RefreshCategoryDropdown(string? filter = null)
        {
            _suppressCategorySelectionChanged = true;
            CategoryListBox.Items.Clear();

            foreach (string cat in _allCategories)
            {
                if (_selectedCategories.Contains(cat))
                    continue;
                if (!string.IsNullOrEmpty(filter) && !cat.Contains(filter, System.StringComparison.OrdinalIgnoreCase))
                    continue;
                CategoryListBox.Items.Add(cat);
            }
            _suppressCategorySelectionChanged = false;
        }

        private void RebuildChips()
        {
            var searchBox = CategorySearchBox;
            CategoryChipsPanel.Children.Clear();

            foreach (string cat in _selectedCategories)
            {
                var chip = new Border
                {
                    Background = new SolidColorBrush(Color.FromRgb(229, 231, 235)),
                    CornerRadius = new CornerRadius(4),
                    Padding = new Thickness(8, 4, 4, 4),
                    Margin = new Thickness(2),
                    VerticalAlignment = VerticalAlignment.Center
                };

                var panel = new StackPanel { Orientation = Orientation.Horizontal };
                panel.Children.Add(new TextBlock
                {
                    Text = cat,
                    VerticalAlignment = VerticalAlignment.Center,
                    FontSize = 12,
                    Foreground = new SolidColorBrush(Color.FromRgb(55, 65, 81))
                });

                var removeBtn = new Button
                {
                    Content = "×",
                    FontSize = 12,
                    Padding = new Thickness(4, 0, 4, 0),
                    Margin = new Thickness(4, 0, 0, 0),
                    BorderThickness = new Thickness(0),
                    Background = Brushes.Transparent,
                    Cursor = System.Windows.Input.Cursors.Hand,
                    VerticalAlignment = VerticalAlignment.Center,
                    Tag = cat
                };
                removeBtn.Click += CategoryChipRemove_Click;
                panel.Children.Add(removeBtn);

                chip.Child = panel;
                CategoryChipsPanel.Children.Add(chip);
            }

            CategoryChipsPanel.Children.Add(searchBox);
        }

        private async void CategoryChipRemove_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string cat)
            {
                _selectedCategories.Remove(cat);
                RebuildChips();
                RefreshCategoryDropdown();
                await LoadProductsAsync();
            }
        }

        private void CategorySearchBox_GotFocus(object sender, RoutedEventArgs e)
        {
            if (string.Equals(CategorySearchBox.Text, CategoryPlaceholder, System.StringComparison.Ordinal))
            {
                CategorySearchBox.Text = string.Empty;
                CategorySearchBox.Foreground = Brushes.Black;
            }
            CategoryPopup.IsOpen = true;
        }

        private void CategorySearchBox_LostFocus(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(CategorySearchBox.Text))
            {
                CategorySearchBox.Text = _selectedCategories.Count > 0 ? string.Empty : CategoryPlaceholder;
                CategorySearchBox.Foreground = Brushes.Gray;
            }
        }

        private void CategorySearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            string text = CategorySearchBox.Text ?? string.Empty;
            if (string.Equals(text, CategoryPlaceholder, System.StringComparison.Ordinal))
                return;
            RefreshCategoryDropdown(text);
            if (!CategoryPopup.IsOpen)
                CategoryPopup.IsOpen = true;
        }

        private async void CategoryListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_suppressCategorySelectionChanged) return;

            foreach (var added in e.AddedItems)
            {
                if (added is string cat && !_selectedCategories.Contains(cat))
                    _selectedCategories.Add(cat);
            }

            CategorySearchBox.Text = string.Empty;
            RebuildChips();
            RefreshCategoryDropdown();
            await LoadProductsAsync();
        }

        private void CategoryDropdownToggle_Click(object sender, RoutedEventArgs e)
        {
            CategoryPopup.IsOpen = !CategoryPopup.IsOpen;
        }

        private void UpdatePaymentUi()
        {
            bool isCash = string.Equals(_selectedPaymentMethod, "Cash", System.StringComparison.OrdinalIgnoreCase);
            AmountReceivedPanel.Visibility = isCash ? Visibility.Visible : Visibility.Collapsed;
            ReferenceNumberPanel.Visibility = isCash ? Visibility.Collapsed : Visibility.Visible;

            if (!isCash)
            {
                AmountReceivedInput.Text = string.Empty;
                ChangeText.Text = string.Empty;
            }
            else
            {
                // clear reference when switching to cash
                ReferenceNumberInput.Text = string.Empty;
                UpdateChangeDisplay();
            }
        }

        private string GetSearchText()
        {
            var txt = SearchBox.Text ?? string.Empty;
            if (string.Equals(txt, SearchPlaceholder, System.StringComparison.Ordinal))
                return string.Empty;
            return txt.Trim();
        }

        private async Task LoadProductsAsync()
        {
            try
            {
                List<string>? cats = _selectedCategories.Count > 0 ? _selectedCategories : null;
                List<PosProduct> products = await _pos_service.GetProductsAsync(cats, GetSearchText());
                ProductsGrid.ItemsSource = products;
                PosMessageText.Text = string.Empty;
            }
            catch (Exception ex)
            {
                PosMessageText.Text = ex.Message;
            }
        }

        private void UpdateCartUi()
        {
            TransactionItemsList.ItemsSource = null;
            TransactionItemsList.ItemsSource = _cart;
            decimal subtotal = _cart.Sum(x => x.LineTotal);
            // total includes 12% VAT
            decimal total = decimal.Round(subtotal * 1.12m, 2);
            SubtotalText.Text = $"Subtotal: {subtotal:N2}";
            TotalText.Text = $"Total: {total:N2}";

            // update change display whenever totals change
            UpdateChangeDisplay();
        }

        private void UpdateChangeDisplay()
        {
            if (!string.Equals(_selectedPaymentMethod, "Cash", System.StringComparison.OrdinalIgnoreCase))
            {
                ChangeText.Text = string.Empty;
                return;
            }

            // total payable includes 12% VAT
            decimal total = decimal.Round(_cart.Sum(i => i.LineTotal) * 1.12m, 2);
            if (decimal.TryParse(AmountReceivedInput.Text.Trim(), NumberStyles.Number, CultureInfo.InvariantCulture, out decimal received))
            {
                decimal change = received - total;
                ChangeText.Text = change >= 0 ? $"Change: {change:N2}" : $"Short: {Math.Abs(change):N2}";
            }
            else
            {
                ChangeText.Text = string.Empty;
            }
        }

        private async void Search_Click(object sender, RoutedEventArgs e)
        {
            await LoadProductsAsync();
        }

        private async void SearchBox_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key != System.Windows.Input.Key.Return) return;
            e.Handled = true;

            string text = GetSearchText();
            if (string.IsNullOrWhiteSpace(text)) return;

            PosProduct? product = await _pos_service.LookupByBarcodeAsync(text);
            if (product is null)
            {
                await LoadProductsAsync();
                return;
            }

            if (!int.TryParse(QuantityInput.Text.Trim(), out int qty) || qty <= 0) qty = 1;

            if (qty > product.StockQty)
            {
                PosMessageText.Text = "Quantity exceeds stock.";
                return;
            }

            PosCartItem? existing = _cart.FirstOrDefault(c => c.ItemId == product.ItemId);
            if (existing is not null)
            {
                if (existing.Quantity + qty > product.StockQty)
                {
                    PosMessageText.Text = "Quantity exceeds stock.";
                    return;
                }
                existing.Quantity += qty;
            }
            else
            {
                _cart.Add(new PosCartItem
                {
                    ItemId = product.ItemId,
                    ItemName = product.ItemName,
                    Quantity = qty,
                    UnitPrice = product.Price
                });
            }

            SearchBox.Text = string.Empty;
            PosMessageText.Text = $"Added {product.ItemName} via barcode.";
            UpdateCartUi();
        }


        private void AddToCart_Click(object sender, RoutedEventArgs e)
        {
            if (ProductsGrid.SelectedItem is not PosProduct selectedProduct)
            {
                PosMessageText.Text = "Select a product first.";
                return;
            }

            if (!int.TryParse(QuantityInput.Text.Trim(), out int qty) || qty <= 0)
            {
                PosMessageText.Text = "Invalid quantity.";
                return;
            }

            if (qty > selectedProduct.StockQty)
            {
                PosMessageText.Text = "Quantity exceeds stock.";
                return;
            }

            PosCartItem? existing = _cart.FirstOrDefault(c => c.ItemId == selectedProduct.ItemId);
            if (existing is not null)
            {
                if (existing.Quantity + qty > selectedProduct.StockQty)
                {
                    PosMessageText.Text = "Quantity exceeds stock.";
                    return;
                }

                existing.Quantity += qty;
            }
            else
            {
                _cart.Add(new PosCartItem
                {
                    ItemId = selectedProduct.ItemId,
                    ItemName = selectedProduct.ItemName,
                    Quantity = qty,
                    UnitPrice = selectedProduct.Price
                });
            }

            PosMessageText.Text = string.Empty;
            UpdateCartUi();
        }

        // Remove item button per row
        private void RemoveCartItem_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is PosCartItem item)
            {
                // require manager authorization before removing
                var auth = new ManagerAuthWindow();
                auth.Owner = Window.GetWindow(this);
                bool? result = auth.ShowDialog();
                if (result == true && auth.IsAuthorized)
                {
                    _cart.Remove(item);
                    PosMessageText.Text = "Item removed.";
                    UpdateCartUi();
                }
                else
                {
                    PosMessageText.Text = "Authorization required to remove item.";
                }
            }
            else
            {
                PosMessageText.Text = "Unable to remove item.";
            }
        }

        // double-click a product to add it to cart using the quantity in QuantityInput
        private void ProductsGrid_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (ProductsGrid.SelectedItem is not PosProduct selectedProduct)
            {
                PosMessageText.Text = "Select a product first.";
                return;
            }

            if (!int.TryParse(QuantityInput.Text.Trim(), out int qty) || qty <= 0)
            {
                PosMessageText.Text = "Invalid quantity.";
                return;
            }

            if (qty > selectedProduct.StockQty)
            {
                PosMessageText.Text = "Quantity exceeds stock.";
                return;
            }

            PosCartItem? existing = _cart.FirstOrDefault(c => c.ItemId == selectedProduct.ItemId);
            if (existing is not null)
            {
                if (existing.Quantity + qty > selectedProduct.StockQty)
                {
                    PosMessageText.Text = "Quantity exceeds stock.";
                    return;
                }

                existing.Quantity += qty;
            }
            else
            {
                _cart.Add(new PosCartItem
                {
                    ItemId = selectedProduct.ItemId,
                    ItemName = selectedProduct.ItemName,
                    Quantity = qty,
                    UnitPrice = selectedProduct.Price
                });
            }

            PosMessageText.Text = string.Empty;
            UpdateCartUi();
        }

        private async void ProcessPayment_Click(object sender, RoutedEventArgs e)
        {
            if (_cart.Count == 0)
            {
                PosMessageText.Text = "Cart is empty.";
                return;
            }

            try
            {
                List<PosCartItem> items = _cart.ToList();
                decimal subtotal = items.Sum(i => i.LineTotal);
                decimal discount = 0m;
                // totalAmount includes 12% VAT
                decimal totalAmount = decimal.Round(subtotal * 1.12m - discount, 2);
                decimal vatableSales = decimal.Round(totalAmount / 1.12m, 2);
                decimal vat = decimal.Round(totalAmount - vatableSales, 2);

                decimal amountReceived = totalAmount;
                decimal change = 0m;
                string referenceNumber = string.Empty;

                if (string.Equals(_selectedPaymentMethod, "Cash", System.StringComparison.OrdinalIgnoreCase))
                {
                    // require valid numeric amount for cash
                    if (!decimal.TryParse(AmountReceivedInput.Text.Trim(), NumberStyles.Number, CultureInfo.InvariantCulture, out amountReceived))
                    {
                        PosMessageText.Text = "Enter a valid amount received.";
                        return;
                    }

                    if (amountReceived < totalAmount)
                    {
                        PosMessageText.Text = "Amount received is less than total.";
                        return;
                    }

                    change = amountReceived - totalAmount;
                }
                else
                {
                    // for Card and E-wallet, require a reference number
                    referenceNumber = ReferenceNumberInput.Text?.Trim() ?? string.Empty;
                    if (string.IsNullOrWhiteSpace(referenceNumber))
                    {
                        PosMessageText.Text = "Enter reference number for card/e-wallet payments.";
                        return;
                    }
                }

                // create transaction in DB
                int transactionId = await _pos_service.CreateTransactionAsync(_user.UserId, _selectedPaymentMethod, items, referenceNumber);

                // clear cart and refresh UI
                _cart.Clear();
                UpdateCartUi();
                await LoadProductsAsync();

                PosMessageText.Foreground = System.Windows.Media.Brushes.DarkGreen;
                PosMessageText.Text = "Payment processed successfully.";

                // show receipt with amount received and change
                var receiptWindow = new ReceiptWindow(
                    transactionId: transactionId,
                    items: items,
                    subTotal: subtotal,
                    vatableSales: vatableSales,
                    vat: vat,
                    totalAmount: totalAmount,
                    paymentMethod: _selectedPaymentMethod,
                    amountReceived: decimal.Round(amountReceived, 2),
                    change: decimal.Round(change, 2),
                    referenceNumber: referenceNumber
                );

                receiptWindow.Owner = Window.GetWindow(this);
                receiptWindow.ShowDialog();
            }
            catch (Exception ex)
            {
                PosMessageText.Foreground = System.Windows.Media.Brushes.DarkRed;
                PosMessageText.Text = ex.Message;
            }
        }

        private void ClearCart_Click(object sender, RoutedEventArgs e)
        {
            // require manager authorization before clearing the entire cart
            var auth = new ManagerAuthWindow();
            auth.Owner = Window.GetWindow(this);
            bool? result = auth.ShowDialog();
            if (result == true && auth.IsAuthorized)
            {
                _cart.Clear();
                UpdateCartUi();
                PosMessageText.Text = "Cart cleared.";
            }
            else
            {
                PosMessageText.Text = "Authorization required to clear cart.";
            }
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

        private void AmountReceivedInput_TextChanged(object sender, TextChangedEventArgs e)
        {
            UpdateChangeDisplay();
        }

        // Open the context menu on button click
        private void PaymentMethodButton_Click(object sender, RoutedEventArgs e)
        {
            if (PaymentMethodButton.ContextMenu is ContextMenu cm)
            {
                cm.PlacementTarget = PaymentMethodButton;
                cm.IsOpen = true;
            }
        }

        // Menu item click handler
        private void PaymentMethodMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem mi && mi.Header is string method)
            {
                _selectedPaymentMethod = method;
                SelectedPaymentText.Text = $"({_selectedPaymentMethod})";
                UpdatePaymentUi();

                // close the context menu if open
                if (PaymentMethodButton.ContextMenu is ContextMenu cm)
                    cm.IsOpen = false;
            }
        }
    }
}
