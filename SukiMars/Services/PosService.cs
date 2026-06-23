using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Data.SqlClient;

namespace SukiMars.Services;

public sealed class PosService
{
    public async Task<List<string>> GetCategoriesAsync()
    {
        const string sql = "SELECT DISTINCT itemCategory FROM dbo.mart_items WHERE itemCategory IS NOT NULL ORDER BY itemCategory";
        var categories = new List<string>();
        await using SqlConnection conn = new(DatabaseConfig.ConnectionString);
        await conn.OpenAsync();
        await using SqlCommand cmd = new(sql, conn);
        await using SqlDataReader r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
            categories.Add(r.GetString(0));
        return categories;
    }

    public async Task<PosProduct?> LookupByBarcodeAsync(string barcode)
    {
        const string sql = """
            SELECT mi.itemID, mi.itemName, mi.itemCode, mi.itemCategory, mi.price, ISNULL(s.currentQty, 0) AS currentQty
            FROM dbo.mart_items mi
            LEFT JOIN dbo.stock s ON s.itemID = mi.itemID
            WHERE mi.barcode = @barcode
            """;

        await using SqlConnection conn = new(DatabaseConfig.ConnectionString);
        await conn.OpenAsync();
        await using SqlCommand cmd = new(sql, conn);
        cmd.Parameters.AddWithValue("@barcode", barcode);
        await using SqlDataReader r = await cmd.ExecuteReaderAsync();
        if (!await r.ReadAsync()) return null;

        return new PosProduct
        {
            ItemId = r.GetInt32(0),
            ItemName = r.GetString(1),
            ItemCode = r.IsDBNull(2) ? string.Empty : r.GetString(2),
            Category = r.IsDBNull(3) ? string.Empty : r.GetString(3),
            Price = r.GetDecimal(4),
            StockQty = r.GetInt32(5)
        };
    }

    public async Task<List<PosProduct>> GetProductsAsync(List<string>? categories = null, string? search = null)
    {
        bool filterByCategory = categories is { Count: > 0 };

        string categoryFilter = filterByCategory
            ? $"mi.itemCategory IN ({string.Join(",", categories!.Select((_, i) => $"@cat{i}"))})"
            : "1=1";

        string query = $"""
            WITH ProductStock AS (
                SELECT 
                    mi.itemID,
                    mi.itemName,
                    mi.itemCode,
                    mi.itemCategory,
                    mi.price,
                    ISNULL(mi.barcode, '') AS barcode,
                    ISNULL((SELECT SUM(currentQty) FROM dbo.stock WHERE itemID = mi.itemID), 0) AS currentQty
                FROM dbo.mart_items mi
            )
            SELECT
                itemID,
                itemName,
                itemCode,
                itemCategory,
                price,
                currentQty,
                barcode
            FROM ProductStock mi
            WHERE ({categoryFilter})
              AND (@search IS NULL OR @search = '' OR mi.itemName LIKE '%' + @search + '%' OR mi.itemCode LIKE '%' + @search + '%' OR mi.barcode LIKE '%' + @search + '%')
              AND currentQty > 0
            ORDER BY mi.itemName ASC
            """;

        List<PosProduct> products = new();
        await using SqlConnection connection = new(DatabaseConfig.ConnectionString);
        await connection.OpenAsync();
        await using SqlCommand command = new(query, connection);
        if (filterByCategory)
            for (int i = 0; i < categories!.Count; i++)
                command.Parameters.AddWithValue($"@cat{i}", categories[i]);
        command.Parameters.AddWithValue("@search", (object?)search ?? DBNull.Value);

        await using SqlDataReader reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            products.Add(new PosProduct
            {
                ItemId = reader.GetInt32(0),
                ItemName = reader.GetString(1),
                ItemCode = reader.IsDBNull(2) ? string.Empty : reader.GetString(2),
                Category = reader.IsDBNull(3) ? string.Empty : reader.GetString(3),
                Price = reader.GetDecimal(4),
                StockQty = reader.GetInt32(5),
                Barcode = reader.IsDBNull(6) ? string.Empty : reader.GetString(6)
            });
        }

        return products;
    }

    public async Task<int> CreateTransactionAsync(int userId, string paymentMethod, IEnumerable<PosCartItem> cartItems, string? referenceNumber = null, decimal discountAmount = 0m, string? discountType = null)
    {
        List<PosCartItem> items = cartItems.ToList();
        if (items.Count == 0)
        {
            throw new InvalidOperationException("Cart is empty.");
        }

        decimal subtotal = items.Sum(i => i.LineTotal);
        int totalItems = items.Sum(i => i.Quantity);
        
        // Prices are VAT-inclusive; compute VAT components
        decimal totalAmount  = decimal.Round(subtotal - discountAmount, 2);
        decimal vat          = decimal.Round(totalAmount * 0.12m, 2);
        decimal vatableSales = decimal.Round(totalAmount - vat, 2);

        await using SqlConnection connection = new(DatabaseConfig.ConnectionString);
        await connection.OpenAsync();
        await using SqlTransaction transaction = connection.BeginTransaction();

        try
        {
            string invoice = $"INV-{DateTime.Now:yyyyMMddHHmmss}";
            const string insertHeaderSql = """
                INSERT INTO dbo.[transaction]
                (
                    transDateTime,
                    discountAmount,
                    discountType,
                    totalItem,
                    totalAmount,
                    VATableSales,
                    VAT,
                    invoiceNumber,
                    referenceNumber,
                    paymentMethod,
                    userID
                )
                VALUES
                (
                    GETDATE(),
                    @discountAmount,
                    @discountType,
                    @totalItem,
                    @totalAmount,
                    @vatableSales,
                    @vat,
                    @invoiceNumber,
                    @referenceNumber,
                    @paymentMethod,
                    @userID
                );
                SELECT CAST(SCOPE_IDENTITY() AS INT);
                """;

            int transactionId;
            await using (SqlCommand header = new(insertHeaderSql, connection, transaction))
            {
                header.Parameters.AddWithValue("@discountAmount", discountAmount);
                header.Parameters.AddWithValue("@discountType", string.IsNullOrWhiteSpace(discountType) || discountType == "None" ? DBNull.Value : discountType);
                header.Parameters.AddWithValue("@totalItem", totalItems);
                header.Parameters.AddWithValue("@totalAmount", totalAmount);
                header.Parameters.AddWithValue("@vatableSales", decimal.Round(vatableSales, 2));
                header.Parameters.AddWithValue("@vat", decimal.Round(vat, 2));
                header.Parameters.AddWithValue("@invoiceNumber", invoice);
                header.Parameters.AddWithValue("@referenceNumber", string.IsNullOrWhiteSpace(referenceNumber) ? DBNull.Value : referenceNumber);
                header.Parameters.AddWithValue("@paymentMethod", paymentMethod);
                header.Parameters.AddWithValue("@userID", userId);
                transactionId = (int)(await header.ExecuteScalarAsync() ?? 0);
            }

            const string insertDetailSql = """
                INSERT INTO dbo.transaction_details (transactionID, itemID, qty, unitPrice, total)
                VALUES (@transactionID, @itemID, @qty, @unitPrice, @total)
                """;
            foreach (PosCartItem item in items)
            {
                await using SqlCommand detail = new(insertDetailSql, connection, transaction);
                detail.Parameters.AddWithValue("@transactionID", transactionId);
                detail.Parameters.AddWithValue("@itemID", item.ItemId);
                detail.Parameters.AddWithValue("@qty", item.Quantity);
                detail.Parameters.AddWithValue("@unitPrice", item.UnitPrice);
                detail.Parameters.AddWithValue("@total", item.LineTotal);
                await detail.ExecuteNonQueryAsync();

                // Deduct stock using FEFO (First-Expired, First-Out)
                int remainingToDeduct = item.Quantity;
                
                const string fetchStockSql = """
                    SELECT s.stockID, s.currentQty
                    FROM dbo.stock s
                    LEFT JOIN dbo.inventory_details id ON s.inventoryDetailsID = id.inventoryDetailsID
                    WHERE s.itemID = @itemID AND s.currentQty > 0
                    ORDER BY 
                        CASE WHEN id.ExpirationDate IS NULL THEN 1 ELSE 0 END, 
                        id.ExpirationDate ASC, 
                        s.stockID ASC
                    """;
                
                var availableBatches = new List<(int StockId, int CurrentQty)>();
                await using (SqlCommand fetchStock = new(fetchStockSql, connection, transaction))
                {
                    fetchStock.Parameters.AddWithValue("@itemID", item.ItemId);
                    await using SqlDataReader reader = await fetchStock.ExecuteReaderAsync();
                    while (await reader.ReadAsync())
                    {
                        availableBatches.Add((reader.GetInt32(0), reader.GetInt32(1)));
                    }
                }

                foreach (var batch in availableBatches)
                {
                    if (remainingToDeduct <= 0) break;

                    int deductFromBatch = Math.Min(batch.CurrentQty, remainingToDeduct);
                    
                    const string deductSql = "UPDATE dbo.stock SET currentQty = currentQty - @qty, lastUpdated = GETDATE() WHERE stockID = @stockID";
                    await using (SqlCommand deductCmd = new(deductSql, connection, transaction))
                    {
                        deductCmd.Parameters.AddWithValue("@qty", deductFromBatch);
                        deductCmd.Parameters.AddWithValue("@stockID", batch.StockId);
                        await deductCmd.ExecuteNonQueryAsync();
                    }

                    remainingToDeduct -= deductFromBatch;
                }
                
                // If oversold (no stock rows had enough), deduct the remaining from the last known batch or create a negative entry
                if (remainingToDeduct > 0 && availableBatches.Count > 0)
                {
                    const string forceDeductSql = "UPDATE dbo.stock SET currentQty = currentQty - @qty, lastUpdated = GETDATE() WHERE stockID = @stockID";
                    await using SqlCommand forceCmd = new(forceDeductSql, connection, transaction);
                    forceCmd.Parameters.AddWithValue("@qty", remainingToDeduct);
                    forceCmd.Parameters.AddWithValue("@stockID", availableBatches.Last().StockId);
                    await forceCmd.ExecuteNonQueryAsync();
                }
                else if (remainingToDeduct > 0)
                {
                    const string insertNegSql = "INSERT INTO dbo.stock (itemID, currentQty, lastUpdated) VALUES (@itemID, -@qty, GETDATE())";
                    await using SqlCommand insertNegCmd = new(insertNegSql, connection, transaction);
                    insertNegCmd.Parameters.AddWithValue("@itemID", item.ItemId);
                    insertNegCmd.Parameters.AddWithValue("@qty", remainingToDeduct);
                    await insertNegCmd.ExecuteNonQueryAsync();
                }
            }

            await transaction.CommitAsync();
            return transactionId;
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    // --------------------------
    // Reporting / Analytics APIs
    // --------------------------

    public sealed record SalesPoint(int X, decimal Amount); // X = hour or day

    // extended product summary with category/price/stock
    public sealed record ProductSalesSummary(int ItemId, string ItemName, string Category, decimal Price, int StockQty, int QtySold, decimal TotalSales)
    {
        public string StockStatus =>
            StockQty == 0 ? "Out of Stock" :
            StockQty <= 12 ? "Low Stock" :
            "In Stock";
    }

    public sealed record SalesByCategory(string Category, decimal Total);

    public sealed record SalesByPayment(string PaymentMethod, decimal Total);

    public sealed record CategorySalesSummary(string Category, int QtySold, decimal TotalSales);

    public sealed record ProductRankSummary(int Rank, string ProductName, string Category, int QtySold, decimal TotalSales);

    public sealed record CategoryRankSummary(int Rank, string Category, int QtySold, decimal TotalSales);

    public sealed record ReportsSummary(decimal TotalSalesThisMonth, int TransactionsThisMonth, decimal AveragePerTransaction, string TopProductName, int InStock, int LowStock, int OutOfStock);

    // Daily: hourly totals for a single date (0..23)
    public async Task<List<SalesPoint>> GetDailySalesAsync(DateTime date)
    {
        const string sql = """
            SELECT DATEPART(HOUR, t.transDateTime) AS hr, ISNULL(SUM(t.totalAmount),0) AS amt
            FROM dbo.[transaction] t
            WHERE CAST(t.transDateTime AS DATE) = @d
            GROUP BY DATEPART(HOUR, t.transDateTime)
            """;

        var points = Enumerable.Range(0, 24).Select(h => new SalesPoint(h, 0m)).ToList();

        await using SqlConnection conn = new(DatabaseConfig.ConnectionString);
        await conn.OpenAsync();
        await using SqlCommand cmd = new(sql, conn);
        cmd.Parameters.AddWithValue("@d", date.Date);
        await using SqlDataReader r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
        {
            int hr = r.GetInt32(0);
            decimal amt = r.IsDBNull(1) ? 0m : r.GetDecimal(1);
            if (hr >= 0 && hr < 24) points[hr] = new SalesPoint(hr, amt);
        }

        return points;
    }

    // Weekly: totals for the week containing the reference date (Sunday..Saturday)
    public async Task<List<SalesPoint>> GetWeeklySalesAsync(DateTime referenceDate)
    {
        // determine week start (Sunday) and end (Saturday)
        DateTime start = referenceDate.Date.AddDays(-(int)referenceDate.DayOfWeek);
        DateTime end = start.AddDays(6);

        var points = Enumerable.Range(0, 7).Select(d => new SalesPoint(d, 0m)).ToList();

        const string sql = """
            SELECT CAST(t.transDateTime AS DATE) AS d, ISNULL(SUM(t.totalAmount),0) AS amt
            FROM dbo.[transaction] t
            WHERE CAST(t.transDateTime AS DATE) BETWEEN @start AND @end
            GROUP BY CAST(t.transDateTime AS DATE)
            """;

        await using SqlConnection conn = new(DatabaseConfig.ConnectionString);
        await conn.OpenAsync();
        await using SqlCommand cmd = new(sql, conn);
        cmd.Parameters.AddWithValue("@start", start.Date);
        cmd.Parameters.AddWithValue("@end", end.Date);
        await using SqlDataReader r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
        {
            DateTime d = r.GetDateTime(0);
            decimal amt = r.IsDBNull(1) ? 0m : r.GetDecimal(1);
            int dow = (int)d.DayOfWeek; // 0 = Sunday .. 6 = Saturday
            if (dow >= 0 && dow < 7) points[dow] = new SalesPoint(dow, amt);
        }

        return points;
    }

    // Range summary for any start/end (used for weekly or custom ranges)
    public async Task<ReportsSummary> GetRangeReportsSummaryAsync(DateTime start, DateTime end)
    {
        const string salesSql = """
            SELECT ISNULL(SUM(totalAmount),0) AS totalSales, ISNULL(COUNT(1),0) AS txCount
            FROM dbo.[transaction]
            WHERE transDateTime BETWEEN @start AND @end
            """;

        decimal totalSales = 0m;
        int txCount = 0;
        await using (SqlConnection conn = new(DatabaseConfig.ConnectionString))
        {
            await conn.OpenAsync();
            await using SqlCommand cmd = new(salesSql, conn);
            cmd.Parameters.AddWithValue("@start", start);
            cmd.Parameters.AddWithValue("@end", end);
            await using SqlDataReader r = await cmd.ExecuteReaderAsync();
            if (await r.ReadAsync())
            {
                totalSales = r.IsDBNull(0) ? 0m : r.GetDecimal(0);
                txCount = r.IsDBNull(1) ? 0 : r.GetInt32(1);
            }
        }

        // determine top product name for the range
        var top = await GetTopProductsAsync(start, end, 1);
        string topName = top.FirstOrDefault()?.ItemName ?? "-";

        decimal avg = txCount == 0 ? 0m : totalSales / txCount;

        // inventory summary (reuse existing logic without date filter)
        int inStock = 0, lowStock = 0, outStock = 0;
        const string invSql = """
                SELECT
                    SUM(CASE WHEN ISNULL(s.currentQty, 0) > 12 THEN 1 ELSE 0 END) AS inStock,
                    SUM(CASE WHEN ISNULL(s.currentQty, 0) BETWEEN 1 AND 12 THEN 1 ELSE 0 END) AS lowStock,
                    SUM(CASE WHEN ISNULL(s.currentQty, 0) = 0 THEN 1 ELSE 0 END) AS outStock
                FROM dbo.mart_items mi
                LEFT JOIN dbo.stock s ON s.itemID = mi.itemID
                """;

        await using (SqlConnection conn = new(DatabaseConfig.ConnectionString))
        {
            await conn.OpenAsync();
            await using SqlCommand cmd = new(invSql, conn);
            await using SqlDataReader ir = await cmd.ExecuteReaderAsync();
            if (await ir.ReadAsync())
            {
                inStock = ir.IsDBNull(0) ? 0 : ir.GetInt32(0);
                lowStock = ir.IsDBNull(1) ? 0 : ir.GetInt32(1);
                outStock = ir.IsDBNull(2) ? 0 : ir.GetInt32(2);
            }
        }

        return new ReportsSummary(totalSales, txCount, decimal.Round(avg, 2), topName, inStock, lowStock, outStock);
    }

    // Sales by payment method for a date range
    public async Task<List<SalesByPayment>> GetSalesByPaymentMethodAsync(DateTime start, DateTime end)
    {
        const string sql = """
            SELECT ISNULL(paymentMethod, 'Cash') AS pm, ISNULL(SUM(totalAmount),0) AS total
            FROM dbo.[transaction]
            WHERE transDateTime BETWEEN @start AND @end
            GROUP BY ISNULL(paymentMethod, 'Cash')
            ORDER BY ISNULL(SUM(totalAmount),0) DESC
            """;

        var result = new List<SalesByPayment>();
        await using SqlConnection conn = new(DatabaseConfig.ConnectionString);
        await conn.OpenAsync();
        await using SqlCommand cmd = new(sql, conn);
        cmd.Parameters.AddWithValue("@start", start);
        cmd.Parameters.AddWithValue("@end", end);
        await using SqlDataReader r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
        {
            string pm = r.IsDBNull(0) ? "Cash" : r.GetString(0);
            decimal total = r.IsDBNull(1) ? 0m : r.GetDecimal(1);
            result.Add(new SalesByPayment(pm, total));
        }

        return result;
    }

    // Monthly: daily totals for a month
    public async Task<List<SalesPoint>> GetMonthlySalesAsync(int year, int month)
    {
        int days = DateTime.DaysInMonth(year, month);
        var points = Enumerable.Range(1, days).Select(d => new SalesPoint(d, 0m)).ToList();

        const string sql = """
            SELECT DAY(t.transDateTime) AS d, ISNULL(SUM(t.totalAmount),0) AS amt
            FROM dbo.[transaction] t
            WHERE YEAR(t.transDateTime) = @y AND MONTH(t.transDateTime) = @m
            GROUP BY DAY(t.transDateTime)
            """;

        await using SqlConnection conn = new(DatabaseConfig.ConnectionString);
        await conn.OpenAsync();
        await using SqlCommand cmd = new(sql, conn);
        cmd.Parameters.AddWithValue("@y", year);
        cmd.Parameters.AddWithValue("@m", month);
        await using SqlDataReader r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
        {
            int d = r.GetInt32(0);
            decimal amt = r.IsDBNull(1) ? 0m : r.GetDecimal(1);
            if (d >= 1 && d <= days) points[d - 1] = new SalesPoint(d, amt);
        }

        return points;
    }

    // Top products in a date range (returns category, price and stock)
    public async Task<List<ProductSalesSummary>> GetTopProductsAsync(DateTime start, DateTime end, int topN = 10)
    {
        const string sql = """
            SELECT 
                td.itemID,
                ISNULL(mi.itemName,'') AS name,
                ISNULL(mi.itemCategory,'') AS category,
                ISNULL(mi.price, 0) AS price,
                ISNULL(MAX(s.currentQty), 0) AS currentQty,
                SUM(td.qty) AS qty,
                SUM(td.total) AS total
            FROM dbo.transaction_details td
            INNER JOIN dbo.[transaction] t ON td.transactionID = t.transactionID
            LEFT JOIN dbo.mart_items mi ON td.itemID = mi.itemID
            LEFT JOIN dbo.stock s ON s.itemID = td.itemID
            WHERE t.transDateTime BETWEEN @start AND @end
            GROUP BY td.itemID, mi.itemName, mi.itemCategory, mi.price
            ORDER BY SUM(td.qty) DESC
            """;

        var list = new List<ProductSalesSummary>();
        await using SqlConnection conn = new(DatabaseConfig.ConnectionString);
        await conn.OpenAsync();
        await using SqlCommand cmd = new(sql, conn);
        cmd.Parameters.AddWithValue("@start", start);
        cmd.Parameters.AddWithValue("@end", end);
        await using SqlDataReader r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
        {
            int id = r.GetInt32(0);
            string name = r.IsDBNull(1) ? string.Empty : r.GetString(1);
            string category = r.IsDBNull(2) ? string.Empty : r.GetString(2);
            decimal price = r.IsDBNull(3) ? 0m : r.GetDecimal(3);
            int stock = r.IsDBNull(4) ? 0 : r.GetInt32(4);
            int qty = r.IsDBNull(5) ? 0 : r.GetInt32(5);
            decimal total = r.IsDBNull(6) ? 0m : r.GetDecimal(6);

            list.Add(new ProductSalesSummary(id, name, category, price, stock, qty, total));
            if (list.Count >= topN) break;
        }

        return list;
    }

    public async Task<List<CategorySalesSummary>> GetTopCategoriesAsync(DateTime start, DateTime end, int topN = 10)
    {
        const string sql = """
            SELECT
                ISNULL(mi.itemCategory, 'Others') AS category,
                SUM(td.qty) AS qty,
                SUM(td.total) AS total
            FROM dbo.transaction_details td
            INNER JOIN dbo.[transaction] t ON td.transactionID = t.transactionID
            LEFT JOIN dbo.mart_items mi ON td.itemID = mi.itemID
            WHERE t.transDateTime BETWEEN @start AND @end
            GROUP BY ISNULL(mi.itemCategory, 'Others')
            ORDER BY SUM(td.qty) DESC
            """;

        var list = new List<CategorySalesSummary>();
        await using SqlConnection conn = new(DatabaseConfig.ConnectionString);
        await conn.OpenAsync();
        await using SqlCommand cmd = new(sql, conn);
        cmd.Parameters.AddWithValue("@start", start);
        cmd.Parameters.AddWithValue("@end", end);
        await using SqlDataReader r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
        {
            string category = r.IsDBNull(0) ? "Others" : r.GetString(0);
            int qty = r.IsDBNull(1) ? 0 : r.GetInt32(1);
            decimal total = r.IsDBNull(2) ? 0m : r.GetDecimal(2);

            list.Add(new CategorySalesSummary(category, qty, total));
            if (list.Count >= topN)
                break;
        }

        return list;
    }

    // Sales by category for a date range
    public async Task<List<SalesByCategory>> GetSalesByCategoryAsync(DateTime start, DateTime end)
    {
        const string sql = """
            SELECT ISNULL(mi.itemCategory, 'Others') AS category, ISNULL(SUM(td.total),0) AS total
            FROM dbo.transaction_details td
            INNER JOIN dbo.[transaction] t ON td.transactionID = t.transactionID
            LEFT JOIN dbo.mart_items mi ON td.itemID = mi.itemID
            WHERE t.transDateTime BETWEEN @start AND @end
            GROUP BY ISNULL(mi.itemCategory, 'Others')
            ORDER BY SUM(td.total) DESC
            """;

        var result = new List<SalesByCategory>();
        await using SqlConnection conn = new(DatabaseConfig.ConnectionString);
        await conn.OpenAsync();
        await using SqlCommand cmd = new(sql, conn);
        cmd.Parameters.AddWithValue("@start", start);
        cmd.Parameters.AddWithValue("@end", end);
        await using SqlDataReader r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
        {
            string cat = r.IsDBNull(0) ? "Others" : r.GetString(0);
            decimal total = r.IsDBNull(1) ? 0m : r.GetDecimal(1);
            result.Add(new SalesByCategory(cat, total));
        }

        return result;
    }

    // Summary: totals for current month + inventory summary
    public async Task<ReportsSummary> GetReportsSummaryAsync(int year, int month)
    {
        const string salesSql = """
            SELECT ISNULL(SUM(totalAmount),0) AS totalSales, ISNULL(COUNT(1),0) AS txCount
            FROM dbo.[transaction]
            WHERE YEAR(transDateTime) = @y AND MONTH(transDateTime) = @m
            """;

        decimal totalSales = 0m;
        int txCount = 0;
        await using (SqlConnection conn = new(DatabaseConfig.ConnectionString))
        {
            await conn.OpenAsync();
            await using SqlCommand cmd = new(salesSql, conn);
            cmd.Parameters.AddWithValue("@y", year);
            cmd.Parameters.AddWithValue("@m", month);

            // use reader, then close it before running another command on same connection
            await using (SqlDataReader r = await cmd.ExecuteReaderAsync())
            {
                if (await r.ReadAsync())
                {
                    totalSales = r.IsDBNull(0) ? 0m : r.GetDecimal(0);
                    txCount = r.IsDBNull(1) ? 0 : r.GetInt32(1);
                }
            } // reader disposed/closed here

            // inventory summary
            const string invSql = """
                SELECT
                    SUM(CASE WHEN ISNULL(s.currentQty, 0) > 12 THEN 1 ELSE 0 END) AS inStock,
                    SUM(CASE WHEN ISNULL(s.currentQty, 0) BETWEEN 1 AND 12 THEN 1 ELSE 0 END) AS lowStock,
                    SUM(CASE WHEN ISNULL(s.currentQty, 0) = 0 THEN 1 ELSE 0 END) AS outStock
                FROM dbo.mart_items mi
                LEFT JOIN dbo.stock s ON s.itemID = mi.itemID
                """;

            await using SqlCommand invCmd = new(invSql, conn);
            await using SqlDataReader ir = await invCmd.ExecuteReaderAsync();
            int inStock = 0, lowStock = 0, outStock = 0;
            if (await ir.ReadAsync())
            {
                inStock = ir.IsDBNull(0) ? 0 : ir.GetInt32(0);
                lowStock = ir.IsDBNull(1) ? 0 : ir.GetInt32(1);
                outStock = ir.IsDBNull(2) ? 0 : ir.GetInt32(2);
            }

            // top product name
            var start = new DateTime(year, month, 1);
            var end = start.AddMonths(1).AddTicks(-1);
            var top = await GetTopProductsAsync(start, end, 1);
            string topName = top.FirstOrDefault()?.ItemName ?? "-";

            decimal avg = txCount == 0 ? 0m : totalSales / txCount;

            return new ReportsSummary(totalSales, txCount, decimal.Round(avg, 2), topName, inStock, lowStock, outStock);
        }
    }

    // New: daily summary for a specific date
    public async Task<ReportsSummary> GetDailyReportsSummaryAsync(DateTime date)
    {
        const string salesSql = """
            SELECT ISNULL(SUM(totalAmount),0) AS totalSales, ISNULL(COUNT(1),0) AS txCount
            FROM dbo.[transaction]
            WHERE CAST(transDateTime AS DATE) = @d
            """;

        decimal totalSales = 0m;
        int txCount = 0;
        await using (SqlConnection conn = new(DatabaseConfig.ConnectionString))
        {
            await conn.OpenAsync();
            await using (SqlCommand cmd = new(salesSql, conn))
            {
                cmd.Parameters.AddWithValue("@d", date.Date);
                await using (SqlDataReader r = await cmd.ExecuteReaderAsync())
                {
                    if (await r.ReadAsync())
                    {
                        totalSales = r.IsDBNull(0) ? 0m : r.GetDecimal(0);
                        txCount = r.IsDBNull(1) ? 0 : r.GetInt32(1);
                    }
                } // reader closed here
            }

            // inventory summary
            const string invSql = """
                SELECT
                    SUM(CASE WHEN ISNULL(s.currentQty, 0) > 12 THEN 1 ELSE 0 END) AS inStock,
                    SUM(CASE WHEN ISNULL(s.currentQty, 0) BETWEEN 1 AND 12 THEN 1 ELSE 0 END) AS lowStock,
                    SUM(CASE WHEN ISNULL(s.currentQty, 0) = 0 THEN 1 ELSE 0 END) AS outStock
                FROM dbo.mart_items mi
                LEFT JOIN dbo.stock s ON s.itemID = mi.itemID
                """;

            await using SqlCommand invCmd = new(invSql, conn);
            await using SqlDataReader ir = await invCmd.ExecuteReaderAsync();
            int inStock = 0, lowStock = 0, outStock = 0;
            if (await ir.ReadAsync())
            {
                inStock = ir.IsDBNull(0) ? 0 : ir.GetInt32(0);
                lowStock = ir.IsDBNull(1) ? 0 : ir.GetInt32(1);
                outStock = ir.IsDBNull(2) ? 0 : ir.GetInt32(2);
            }

            // top product for the day
            var start = date.Date;
            var end = start.AddDays(1).AddTicks(-1);
            var top = await GetTopProductsAsync(start, end, 1);
            string topName = top.FirstOrDefault()?.ItemName ?? "-";

            decimal avg = txCount == 0 ? 0m : totalSales / txCount;

            return new ReportsSummary(totalSales, txCount, decimal.Round(avg, 2), topName, inStock, lowStock, outStock);
        }
    }
}

public sealed class PosProduct
{
    public int ItemId { get; init; }
    public string ItemName { get; init; } = string.Empty;
    public string ItemCode { get; init; } = string.Empty;
    public string Category { get; init; } = string.Empty;
    public decimal Price { get; init; }
    public int StockQty { get; init; }
    public string Barcode { get; init; } = string.Empty;
    public string StockStatus =>
        StockQty == 0 ? "Out of Stock" :
        StockQty <= 12 ? "Low Stock" :
        "In Stock";

    private BitmapSource? _barcodeImage;
    public BitmapSource? BarcodeImage
    {
        get
        {
            if (_barcodeImage is not null) return _barcodeImage;
            if (string.IsNullOrWhiteSpace(Barcode)) return null;
            _barcodeImage = Code128Generator.Generate(Barcode, 200, 40);
            return _barcodeImage;
        }
    }
}

public static class Code128Generator
{
    private static readonly int[][] Patterns =
    {
        new[]{2,1,2,2,2,2}, new[]{2,2,2,1,2,2}, new[]{2,2,2,2,2,1}, new[]{1,2,1,2,2,3},
        new[]{1,2,1,3,2,2}, new[]{1,3,1,2,2,2}, new[]{1,2,2,2,1,3}, new[]{1,2,2,3,1,2},
        new[]{1,3,2,2,1,2}, new[]{2,2,1,2,1,3}, new[]{2,2,1,3,1,2}, new[]{2,3,1,2,1,2},
        new[]{1,1,2,2,3,2}, new[]{1,2,2,1,3,2}, new[]{1,2,2,2,3,1}, new[]{1,1,3,2,2,2},
        new[]{1,2,3,1,2,2}, new[]{1,2,3,2,2,1}, new[]{2,2,3,2,1,1}, new[]{2,2,1,1,3,2},
        new[]{2,2,1,2,3,1}, new[]{2,1,3,2,1,2}, new[]{2,2,3,1,1,2}, new[]{3,1,2,1,3,1},
        new[]{3,1,1,2,2,2}, new[]{3,2,1,1,2,2}, new[]{3,2,1,2,2,1}, new[]{3,1,2,2,1,2},
        new[]{3,2,2,1,1,2}, new[]{3,2,2,2,1,1}, new[]{2,1,2,1,2,3}, new[]{2,1,2,3,2,1},
        new[]{2,3,2,1,2,1}, new[]{1,1,1,3,2,3}, new[]{1,3,1,1,2,3}, new[]{1,3,1,3,2,1},
        new[]{1,1,2,3,2,3}, new[]{1,3,2,1,2,3}, new[]{1,3,2,3,2,1}, new[]{2,1,1,3,2,3},
        new[]{2,3,1,1,2,3}, new[]{2,3,1,3,2,1}, new[]{1,1,2,1,3,3}, new[]{1,1,2,3,3,1},
        new[]{1,3,2,1,3,1}, new[]{1,1,3,1,2,3}, new[]{1,1,3,3,2,1}, new[]{1,3,3,1,2,1},
        new[]{3,1,3,1,2,1}, new[]{2,1,1,3,3,1}, new[]{2,3,1,1,3,1}, new[]{2,1,3,1,1,3},
        new[]{2,1,3,3,1,1}, new[]{2,1,3,1,3,1}, new[]{3,1,1,1,2,3}, new[]{3,1,1,3,2,1},
        new[]{3,3,1,1,2,1}, new[]{3,1,2,1,1,3}, new[]{3,1,2,3,1,1}, new[]{3,3,2,1,1,1},
        new[]{3,1,4,1,1,1}, new[]{2,2,1,4,1,1}, new[]{4,3,1,1,1,1}, new[]{1,1,1,2,2,4},
        new[]{1,1,1,4,2,2}, new[]{1,2,1,1,2,4}, new[]{1,2,1,4,2,1}, new[]{1,4,1,1,2,2},
        new[]{1,4,1,2,2,1}, new[]{1,1,2,2,1,4}, new[]{1,1,2,4,1,2}, new[]{1,2,2,1,1,4},
        new[]{1,2,2,4,1,1}, new[]{1,4,2,1,1,2}, new[]{1,4,2,2,1,1}, new[]{2,4,1,2,1,1},
        new[]{2,2,1,1,1,4}, new[]{4,1,3,1,1,1}, new[]{2,4,1,1,1,2}, new[]{1,3,4,1,1,1},
        new[]{1,1,1,2,4,2}, new[]{1,2,1,1,4,2}, new[]{1,2,1,2,4,1}, new[]{1,1,4,2,1,2},
        new[]{1,2,4,1,1,2}, new[]{1,2,4,2,1,1}, new[]{4,1,1,2,1,2}, new[]{4,2,1,1,1,2},
        new[]{4,2,1,2,1,1}, new[]{2,1,2,1,4,1}, new[]{2,1,4,1,2,1}, new[]{4,1,2,1,2,1},
        new[]{1,1,1,1,4,3}, new[]{1,1,1,3,4,1}, new[]{1,3,1,1,4,1}, new[]{1,1,4,1,1,3},
        new[]{1,1,4,3,1,1}, new[]{4,1,1,1,1,3}, new[]{4,1,1,3,1,1}, new[]{1,1,3,1,4,1},
        new[]{1,1,4,1,3,1}, new[]{3,1,1,1,4,1}, new[]{4,1,1,1,3,1}, new[]{2,1,1,4,1,2},
        new[]{2,1,1,2,1,4}, new[]{2,1,1,2,3,2}
    };

    private static readonly int[] StopPattern = { 2, 3, 3, 1, 1, 1, 2 };

    public static BitmapSource Generate(string data, int width, int height)
    {
        int startCode = 104; // Code B
        var codes = new List<int> { startCode };
        foreach (char c in data)
            codes.Add(c - 32);

        int checksum = startCode;
        for (int i = 1; i < codes.Count; i++)
            checksum += codes[i] * i;
        codes.Add(checksum % 103);

        var bars = new List<int>();
        foreach (int code in codes)
            bars.AddRange(Patterns[code]);
        bars.AddRange(StopPattern);

        int totalUnits = 0;
        foreach (int b in bars) totalUnits += b;

        double unitWidth = (double)width / totalUnits;
        int pixelHeight = height;

        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, width, pixelHeight));
            double x = 0;
            bool black = true;
            foreach (int b in bars)
            {
                double w = b * unitWidth;
                if (black)
                    dc.DrawRectangle(Brushes.Black, null, new Rect(x, 0, w, pixelHeight));
                x += w;
                black = !black;
            }
        }

        var rtb = new RenderTargetBitmap(width, pixelHeight, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(visual);
        rtb.Freeze();
        return rtb;
    }

    public static string SaveToPng(string data, string outputPath, int width = 400, int height = 100)
    {
        BitmapSource src = Generate(data, width, height);
        var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
        encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(src));
        using var fs = new System.IO.FileStream(outputPath, System.IO.FileMode.Create);
        encoder.Save(fs);
        return outputPath;
    }
}

public sealed class PosCartItem
{
    public int ItemId { get; init; }
    public string ItemName { get; init; } = string.Empty;
    public int Quantity { get; set; }
    public decimal UnitPrice { get; init; }
    public decimal LineTotal => UnitPrice * Quantity;
}
