using Microsoft.Data.SqlClient;

namespace SukiMars.Services;

public sealed class PosService
{
    public async Task<List<PosProduct>> GetProductsAsync(string? category = null, string? search = null)
    {
        const string query = """
            SELECT
                mi.itemID,
                mi.itemName,
                mi.itemCode,
                mi.itemCategory,
                mi.price,
                ISNULL(s.currentQty, 0) AS currentQty
            FROM dbo.mart_items mi
            LEFT JOIN dbo.stock s ON s.itemID = mi.itemID
            WHERE (@category IS NULL OR @category = '' OR @category = 'All' OR mi.itemCategory = @category)
              AND (@search IS NULL OR @search = '' OR mi.itemName LIKE '%' + @search + '%' OR mi.itemCode LIKE '%' + @search + '%')
            ORDER BY mi.itemName ASC
            """;

        List<PosProduct> products = [];
        await using SqlConnection connection = new(DatabaseConfig.ConnectionString);
        await connection.OpenAsync();
        await using SqlCommand command = new(query, connection);
        command.Parameters.AddWithValue("@category", (object?)category ?? DBNull.Value);
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
                StockQty = reader.GetInt32(5)
            });
        }

        return products;
    }

    public async Task<int> CreateTransactionAsync(int userId, string paymentMethod, IEnumerable<PosCartItem> cartItems)
    {
        List<PosCartItem> items = cartItems.ToList();
        if (items.Count == 0)
        {
            throw new InvalidOperationException("Cart is empty.");
        }

        decimal subtotal = items.Sum(i => i.LineTotal);
        int totalItems = items.Sum(i => i.Quantity);
        decimal discount = 0m;
        decimal totalAmount = subtotal - discount;
        decimal vatableSales = totalAmount / 1.12m;
        decimal vat = totalAmount - vatableSales;

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
                    totalItem,
                    totalAmount,
                    VATableSales,
                    VAT,
                    invoiceNumber,
                    paymentMethod,
                    userID
                )
                VALUES
                (
                    GETDATE(),
                    @discountAmount,
                    @totalItem,
                    @totalAmount,
                    @vatableSales,
                    @vat,
                    @invoiceNumber,
                    @paymentMethod,
                    @userID
                );
                SELECT CAST(SCOPE_IDENTITY() AS INT);
                """;

            int transactionId;
            await using (SqlCommand header = new(insertHeaderSql, connection, transaction))
            {
                header.Parameters.AddWithValue("@discountAmount", discount);
                header.Parameters.AddWithValue("@totalItem", totalItems);
                header.Parameters.AddWithValue("@totalAmount", totalAmount);
                header.Parameters.AddWithValue("@vatableSales", decimal.Round(vatableSales, 2));
                header.Parameters.AddWithValue("@vat", decimal.Round(vat, 2));
                header.Parameters.AddWithValue("@invoiceNumber", invoice);
                header.Parameters.AddWithValue("@paymentMethod", paymentMethod);
                header.Parameters.AddWithValue("@userID", userId);
                transactionId = (int)(await header.ExecuteScalarAsync() ?? 0);
            }

            const string insertDetailSql = """
                INSERT INTO dbo.transaction_details (transactionID, itemID, qty, unitPrice, total)
                VALUES (@transactionID, @itemID, @qty, @unitPrice, @total)
                """;
            const string updateStockSql = """
                UPDATE dbo.stock
                SET currentQty = currentQty - @qty,
                    lastUpdated = GETDATE()
                WHERE itemID = @itemID
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

                await using SqlCommand stock = new(updateStockSql, connection, transaction);
                stock.Parameters.AddWithValue("@itemID", item.ItemId);
                stock.Parameters.AddWithValue("@qty", item.Quantity);
                await stock.ExecuteNonQueryAsync();
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
}

public sealed class PosProduct
{
    public int ItemId { get; init; }
    public string ItemName { get; init; } = string.Empty;
    public string ItemCode { get; init; } = string.Empty;
    public string Category { get; init; } = string.Empty;
    public decimal Price { get; init; }
    public int StockQty { get; init; }
    public string StockStatus =>
        StockQty == 0 ? "Out of Stock" :
        StockQty <= 12 ? "Low Stock" :
        "In Stock";
}

public sealed class PosCartItem
{
    public int ItemId { get; init; }
    public string ItemName { get; init; } = string.Empty;
    public int Quantity { get; set; }
    public decimal UnitPrice { get; init; }
    public decimal LineTotal => UnitPrice * Quantity;
}
