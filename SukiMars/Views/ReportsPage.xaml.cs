using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using SukiMars.Services;

namespace SukiMars.Views
{
    public partial class ReportsPage : Page
    {
        private readonly PosService _posService = new();
        private const double ChartHeight = 320;
        private const double ChartLeftPad = 72;
        private const double ChartRightPad = 24;
        private const double ChartTopPad = 24;
        private const double ChartBottomPad = 48;

        public ReportsPage()
        {
            InitializeComponent();
        // wire up range dropdown (lookup by name to avoid direct generated field dependency)
        var rc = this.FindName("RangeCombo") as ComboBox;
        if (rc != null)
            rc.SelectionChanged += RangeCombo_SelectionChanged;

        // Range default handled by XAML SelectedIndex
        Loaded += ReportsPage_Loaded;
        }

        private void ReportsPage_Loaded(object? sender, RoutedEventArgs e)
        {
            _ = RefreshAsync();
        }

        private async void RangeChanged(object sender, RoutedEventArgs e) => await RefreshAsync();

        private async void RangeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!IsLoaded) return;
            await RefreshAsync();
        }

        private async Task RefreshAsync()
        {
            try
            {
                DateTime now = DateTime.Now;

                var dailyTask = _posService.GetDailyReportsSummaryAsync(now.Date);
                var monthlyTask = _posService.GetReportsSummaryAsync(now.Year, now.Month);

                DateTime start, end;
                List<PosService.SalesPoint> points;
                // Determine selected range (Daily, Weekly, Monthly)
                var rangeCombo = this.FindName("RangeCombo") as ComboBox;
                string selectedRange = (rangeCombo?.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "Monthly";
                bool isDailyRange = false;

                if (string.Equals(selectedRange, "Daily", StringComparison.OrdinalIgnoreCase))
                {
                    isDailyRange = true;
                    start = now.Date;
                    end = start.AddDays(1).AddTicks(-1);
                    points = await _posService.GetDailySalesAsync(now.Date);
                }
                else if (string.Equals(selectedRange, "Weekly", StringComparison.OrdinalIgnoreCase))
                {
                    // weekly: week containing today, Sun..Sat
                    isDailyRange = false;
                    points = await _posService.GetWeeklySalesAsync(now.Date);
                    DateTime weekStart = now.Date.AddDays(-(int)now.DayOfWeek);
                    DateTime weekEnd = weekStart.AddDays(6);
                    start = weekStart;
                    end = weekEnd.AddDays(1).AddTicks(-1);
                }
                else
                {
                    // Monthly (default)
                    isDailyRange = false;
                    start = new DateTime(now.Year, now.Month, 1);
                    end = start.AddMonths(1).AddTicks(-1);
                    points = await _posService.GetMonthlySalesAsync(now.Year, now.Month);
                }

                // Always show monthly header and monthly transactions label regardless of selection
                ChartTitle.Text = $"Total Sales — {now:MMMM yyyy}";
                TransactionsLabel.Text = "Transactions this Month";

                var topCategoriesTask = _posService.GetTopCategoriesAsync(start, end, 10);
                var topProductsTask = _posService.GetTopProductsAsync(start, end, 10);
                var monthlyPointsTask = _posService.GetMonthlySalesAsync(now.Year, now.Month);

                await Task.WhenAll(dailyTask, monthlyTask, topCategoriesTask, topProductsTask, monthlyPointsTask);

                var dailySummary = await dailyTask;
                var monthlySummary = await monthlyTask;
                var topCategories = await topCategoriesTask;
                var topProducts = await topProductsTask;
                var monthlyPoints = await monthlyPointsTask;

                // Title suffix based on selected range
                string titleSuffix;
                if (string.Equals(selectedRange, "Daily", StringComparison.OrdinalIgnoreCase))
                {
                    titleSuffix = "Today";
                }
                else if (string.Equals(selectedRange, "Weekly", StringComparison.OrdinalIgnoreCase))
                {
                    titleSuffix = "This Week";
                }
                else
                {
                    titleSuffix = "This Month";
                }

                TopCategoriesTitle.Text = $"Top 10 Best Selling Categories ({titleSuffix})";
                TopProductsTitle.Text = $"Top 10 Best Selling Products ({titleSuffix})";

                TopCategoriesGrid.ItemsSource = topCategories
                    .Select((c, i) => new PosService.CategoryRankSummary(i + 1, c.Category, c.QtySold, c.TotalSales))
                    .ToList();

                TopProductsGrid.ItemsSource = topProducts
                    .Select((p, i) => new PosService.ProductRankSummary(i + 1, p.ItemName, p.Category, p.QtySold, p.TotalSales))
                    .ToList();

                // Chart period totals remain monthly
                decimal periodTotal = monthlySummary.TotalSalesThisMonth;
                decimal monthlyTotal = monthlySummary.TotalSalesThisMonth;

                ChartTotalText.Text = $"₱{periodTotal:N2}";
                // Monthly ratio text removed

                // Determine main summary based on selected range and update the 4 summary boxes
                PosService.ReportsSummary mainSummary;
                if (string.Equals(selectedRange, "Daily", StringComparison.OrdinalIgnoreCase))
                {
                    // dailySummary already awaited above
                    mainSummary = dailySummary;
                }
                else if (string.Equals(selectedRange, "Weekly", StringComparison.OrdinalIgnoreCase))
                {
                    // weekly summary for start..end
                    mainSummary = await _posService.GetRangeReportsSummaryAsync(start, end);
                }
                else
                {
                    mainSummary = monthlySummary;
                }

                // Update Transactions label according to selected range
                if (string.Equals(selectedRange, "Daily", StringComparison.OrdinalIgnoreCase))
                {
                    TransactionsLabel.Text = "Transactions Today";
                }
                else if (string.Equals(selectedRange, "Weekly", StringComparison.OrdinalIgnoreCase))
                {
                    TransactionsLabel.Text = "Transactions this Week";
                }
                else
                {
                    TransactionsLabel.Text = "Transactions this Month";
                }

                TotalSalesText.Text = $"₱{mainSummary.TotalSalesThisMonth:N2}";
                TransactionsText.Text = mainSummary.TransactionsThisMonth.ToString();
                AvgText.Text = $"₱{mainSummary.AveragePerTransaction:N2}";
                TopProductText.Text = mainSummary.TopProductName;


                // Render monthly points only (same regardless of selection)
                RenderChart(monthlyPoints, monthlyPoints, false, periodTotal, monthlyTotal, true, "Monthly");
            }
            catch (Exception ex)
            {
                Debug.WriteLine("ReportsPage.RefreshAsync exception:");
                Debug.WriteLine(ex.ToString());
                TopProductText.Text = $"Error: {ex.GetType().Name}: {ex.Message}";
            }
        }



        private void RenderChart(
            List<PosService.SalesPoint> points,
            List<PosService.SalesPoint> monthlyPoints,
            bool isDailyRange,
            decimal periodTotal,
            decimal monthlyTotal,
            bool isMonthlyOnly,
            string selectedRange)
        {
            ChartCanvas.Children.Clear();
            // Chart type fixed to Line but only allowed for Monthly range
            bool isBar = false;
            double plotHeight = ChartHeight - ChartTopPad - ChartBottomPad;
            double minPointWidth = isDailyRange ? 28 : 22;
            double plotWidth = Math.Max(400, Math.Max(points?.Count ?? 0, monthlyPoints?.Count ?? 0) * minPointWidth + ChartLeftPad + ChartRightPad + 40);
            ChartCanvas.Width = plotWidth;
            ChartCanvas.Height = ChartHeight;
            decimal maxAmount = 0m;
            if (points != null && points.Count > 0) maxAmount = Math.Max(maxAmount, points.Max(p => p.Amount));
            if (monthlyPoints != null && monthlyPoints.Count > 0) maxAmount = Math.Max(maxAmount, monthlyPoints.Max(p => p.Amount));
            maxAmount = Math.Max(maxAmount, monthlyTotal);
            if (maxAmount <= 0)
                maxAmount = 1;

            DrawYAxis(plotHeight, (double)maxAmount);
            // X-axis label: daily uses hour, weekly uses weekday labels, monthly uses day of month
            string xAxisLabel = isDailyRange ? "Hour of Day" : (string.Equals(selectedRange, "Weekly", StringComparison.OrdinalIgnoreCase) ? "Day of Week" : "Day of Month");
            DrawXAxisLabel(xAxisLabel, plotWidth, plotHeight);

            // Always draw the monthly line (if available)
            if (monthlyPoints != null && monthlyPoints.Count > 0)
            {
                RenderTimeSeriesLine(monthlyPoints, plotWidth, plotHeight, (double)maxAmount, false);
            }

            // Additionally overlay the selected-range series if it's different from monthly
            if (points != null && points.Count > 0)
            {
                // For Daily, points are 24 hourly values; for Weekly, 7 daily values
                RenderTimeSeriesLine(points, plotWidth, plotHeight, (double)maxAmount, isDailyRange);
            }

            var totalLabel = new TextBlock
            {
                Text = $"Period total: ₱{periodTotal:N2}",
                FontSize = 11,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Color.FromRgb(0x0B, 0x6B, 0x3C))
            };
            Canvas.SetLeft(totalLabel, ChartLeftPad);
            Canvas.SetTop(totalLabel, 4);
            ChartCanvas.Children.Add(totalLabel);
        }

        private void DrawYAxis(double plotHeight, double maxAmount)
        {
            double originY = ChartTopPad + plotHeight;

            var yAxis = new Line
            {
                X1 = ChartLeftPad,
                Y1 = ChartTopPad,
                X2 = ChartLeftPad,
                Y2 = originY,
                Stroke = new SolidColorBrush(Color.FromRgb(0x9C, 0xA3, 0xAF)),
                StrokeThickness = 1
            };
            ChartCanvas.Children.Add(yAxis);

            for (int i = 0; i <= 4; i++)
            {
                double ratio = i / 4.0;
                double y = originY - ratio * plotHeight;
                decimal value = (decimal)(ratio * maxAmount);

                var gridLine = new Line
                {
                    X1 = ChartLeftPad,
                    Y1 = y,
                    X2 = ChartCanvas.Width - ChartRightPad,
                    Y2 = y,
                    Stroke = new SolidColorBrush(Color.FromRgb(0xE5, 0xE7, 0xEB)),
                    StrokeThickness = 1
                };
                ChartCanvas.Children.Add(gridLine);

                var label = new TextBlock
                {
                    Text = FormatPeso(value),
                    FontSize = 10,
                    Foreground = new SolidColorBrush(Color.FromRgb(0x6B, 0x72, 0x80)),
                    Width = ChartLeftPad - 8,
                    TextAlignment = TextAlignment.Right
                };
                Canvas.SetLeft(label, 0);
                Canvas.SetTop(label, y - 8);
                ChartCanvas.Children.Add(label);
            }
        }

        private void DrawXAxisLabel(string label, double plotWidth, double plotHeight)
        {
            double originY = ChartTopPad + plotHeight;

            var xAxis = new Line
            {
                X1 = ChartLeftPad,
                Y1 = originY,
                X2 = plotWidth - ChartRightPad,
                Y2 = originY,
                Stroke = new SolidColorBrush(Color.FromRgb(0x9C, 0xA3, 0xAF)),
                StrokeThickness = 1
            };
            ChartCanvas.Children.Add(xAxis);

            var axisLabel = new TextBlock
            {
                Text = label,
                FontSize = 11,
                Foreground = new SolidColorBrush(Color.FromRgb(0x6B, 0x72, 0x80))
            };
            Canvas.SetLeft(axisLabel, ChartLeftPad);
            Canvas.SetTop(axisLabel, originY + 28);
            ChartCanvas.Children.Add(axisLabel);
        }

        private void RenderComparisonBars(
            List<PosService.SalesPoint> comparisonPoints,
            double plotWidth,
            double plotHeight,
            double maxAmount)
        {
            double originY = ChartTopPad + plotHeight;
            double usableWidth = plotWidth - ChartLeftPad - ChartRightPad;
            double barWidth = Math.Min(120, usableWidth / 4);
            double gap = (usableWidth - barWidth * 2) / 3;
            string[] labels = ["Daily Total", "Monthly Total"];
            var colors = new[] { Color.FromRgb(16, 185, 129), Color.FromRgb(59, 130, 246) };

            for (int i = 0; i < comparisonPoints.Count; i++)
            {
                var point = comparisonPoints[i];
                double x = ChartLeftPad + gap + i * (barWidth + gap);
                double h = (double)(point.Amount / (decimal)maxAmount) * plotHeight;

                var rect = new Rectangle
                {
                    Width = barWidth,
                    Height = Math.Max(h, point.Amount > 0 ? 4 : 0),
                    Fill = new SolidColorBrush(colors[i]),
                    RadiusX = 4,
                    RadiusY = 4
                };
                Canvas.SetLeft(rect, x);
                Canvas.SetTop(rect, originY - rect.Height);
                ChartCanvas.Children.Add(rect);

                var amountLabel = new TextBlock
                {
                    Text = $"₱{point.Amount:N2}",
                    FontSize = 11,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = new SolidColorBrush(Color.FromRgb(0x11, 0x18, 0x27)),
                    Width = barWidth + 20,
                    TextAlignment = TextAlignment.Center
                };
                Canvas.SetLeft(amountLabel, x - 10);
                Canvas.SetTop(amountLabel, originY - rect.Height - 20);
                ChartCanvas.Children.Add(amountLabel);

                var xLabel = new TextBlock
                {
                    Text = labels[i],
                    FontSize = 11,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = new SolidColorBrush(Color.FromRgb(0x37, 0x41, 0x51)),
                    Width = barWidth + 20,
                    TextAlignment = TextAlignment.Center
                };
                Canvas.SetLeft(xLabel, x - 10);
                Canvas.SetTop(xLabel, originY + 8);
                ChartCanvas.Children.Add(xLabel);
            }
        }

        private void RenderTimeSeriesLine(
            List<PosService.SalesPoint> points,
            double plotWidth,
            double plotHeight,
            double maxAmount,
            bool isDailyRange)
        {
            if (points == null || points.Count == 0)
                return;

            double originY = ChartTopPad + plotHeight;
            double usableWidth = plotWidth - ChartLeftPad - ChartRightPad;
            double step = usableWidth / Math.Max(1, points.Count - 1);

            var poly = new Polyline
            {
                Stroke = new SolidColorBrush(Color.FromRgb(59, 130, 246)),
                StrokeThickness = 2.5
            };

            for (int i = 0; i < points.Count; i++)
            {
                var point = points[i];
                double x = ChartLeftPad + i * step;
                double y = originY - ((double)(point.Amount / (decimal)maxAmount) * plotHeight);
                poly.Points.Add(new Point(x, y));

                if (point.Amount > 0)
                {
                    var dot = new Ellipse
                    {
                        Width = 7,
                        Height = 7,
                        Fill = Brushes.White,
                        Stroke = new SolidColorBrush(Color.FromRgb(59, 130, 246)),
                        StrokeThickness = 2
                    };
                    Canvas.SetLeft(dot, x - 3.5);
                    Canvas.SetTop(dot, y - 3.5);
                    ChartCanvas.Children.Add(dot);
                }

                bool showLabel = isDailyRange
                    ? i % 3 == 0 || i == points.Count - 1
                    : i % 5 == 0 || i == points.Count - 1;

                if (showLabel)
                {
                    string xText = isDailyRange ? FormatHour(point.X) : point.X.ToString(CultureInfo.InvariantCulture);
                    var xLabel = new TextBlock
                    {
                        Text = xText,
                        FontSize = 10,
                        Foreground = new SolidColorBrush(Color.FromRgb(0x6B, 0x72, 0x80)),
                        Width = 36,
                        TextAlignment = TextAlignment.Center
                    };
                    Canvas.SetLeft(xLabel, x - 18);
                    Canvas.SetTop(xLabel, originY + 8);
                    ChartCanvas.Children.Add(xLabel);
                }
            }

            ChartCanvas.Children.Add(poly);
        }

        private static string FormatPeso(decimal amount)
        {
            if (amount >= 1_000_000)
                return $"₱{amount / 1_000_000m:0.#}M";
            if (amount >= 1_000)
                return $"₱{amount / 1_000m:0.#}K";
            return $"₱{amount:0}";
        }

        private static string FormatHour(int hour) =>
            hour switch
            {
                0 => "12AM",
                12 => "12PM",
                < 12 => $"{hour}AM",
                _ => $"{hour - 12}PM"
            };

        // Sales-by-category/payment pie chart and legend were removed from the UI.
    }
}
