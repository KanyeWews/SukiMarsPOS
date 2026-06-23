using System;
using System.Collections.Generic;
using System.Windows;
using SukiMars.Services;
using System.Globalization;

namespace SukiMars.Views
{
    public partial class ReceiptWindow : Window
    {
        public ReceiptWindow(int transactionId, List<PosCartItem> items, decimal subTotal, decimal vatableSales, decimal vat, decimal totalAmount, string paymentMethod, decimal amountReceived, decimal change, string referenceNumber, decimal discountAmount = 0m, string? discountType = null)
        {
            InitializeComponent();

            // populate header with real time
            DateText.Text = $"Date: {DateTime.Now:yyyy-MM-dd}    {DateTime.Now:HH:mm:ss}";

            // populate items
            ItemsList.ItemsSource = items;

            // populate totals (right-aligned in XAML)
            SubTotalText.Text = subTotal.ToString("N2", CultureInfo.InvariantCulture);
            
            if (discountAmount > 0)
            {
                DiscountLabelText.Visibility = Visibility.Visible;
                DiscountValueText.Visibility = Visibility.Visible;
                DiscountLabelText.Text = $"Discount ({discountType})";
                DiscountValueText.Text = $"-{discountAmount.ToString("N2", CultureInfo.InvariantCulture)}";
            }
            
            VatableText.Text = vatableSales.ToString("N2", CultureInfo.InvariantCulture);
            VatText.Text = vat.ToString("N2", CultureInfo.InvariantCulture);
            TotalText.Text = totalAmount.ToString("N2", CultureInfo.InvariantCulture);

            // populate amount received & change (aligns to right)
            if (string.Equals(paymentMethod, "Cash", StringComparison.OrdinalIgnoreCase))
            {
                AmountReceivedText.Text = amountReceived.ToString("N2", CultureInfo.InvariantCulture);
                ChangeText.Text = change.ToString("N2", CultureInfo.InvariantCulture);
            }
            else
            {
                AmountReceivedText.Text = string.Empty;
                ChangeText.Text = string.Empty;
            }

            // Payment method displayed at the bottom (lowest section)
            PaymentMethodText.Text = $"Payment Method: {paymentMethod}";

            // show reference number for non-cash payments
            if (!string.Equals(paymentMethod, "Cash", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(referenceNumber))
            {
                PaymentMethodText.Text += $"  |  Ref: {referenceNumber}";
            }
        }
    }
}
