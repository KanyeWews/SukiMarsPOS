using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using SukiMars.Services;

namespace SukiMars.Views
{
    public partial class CashierPosPage : Page
    {
        private readonly UserSession _user;
        private readonly PosService _posService = new();
        private readonly List<PosCartItem> _cart = [];
        private string _selectedCategory = "All";
        private const string SearchPlaceholder = "Search product...";

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

            await LoadProductsAsync();
            UpdateCartUi();
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
                List<PosProduct> products = await _posService.GetProductsAsync(_selectedCategory, GetSearchText());
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
            SubtotalText.Text = $"Subtotal: {subtotal:N2}";
            TotalText.Text = $"Total: {subtotal:N2}";
        }

        private async void Search_Click(object sender, RoutedEventArgs e)
        {
            await LoadProductsAsync();
        }

        private async void Category_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button { Tag: string category })
            {
                _selectedCategory = category;
            }

            await LoadProductsAsync();
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

        private async void ProcessPayment_Click(object sender, RoutedEventArgs e)
        {
            if (_cart.Count == 0)
            {
                PosMessageText.Text = "Cart is empty.";
                return;
            }

            try
            {
                await _posService.CreateTransactionAsync(_user.UserId, "Cash", _cart);
                _cart.Clear();
                UpdateCartUi();
                await LoadProductsAsync();
                PosMessageText.Foreground = System.Windows.Media.Brushes.DarkGreen;
                PosMessageText.Text = "Payment processed successfully.";
            }
            catch (Exception ex)
            {
                PosMessageText.Foreground = System.Windows.Media.Brushes.DarkRed;
                PosMessageText.Text = ex.Message;
            }
        }

        private void ClearCart_Click(object sender, RoutedEventArgs e)
        {
            _cart.Clear();
            UpdateCartUi();
            PosMessageText.Text = "Cart cleared.";
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
