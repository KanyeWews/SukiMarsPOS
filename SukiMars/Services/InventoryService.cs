using Microsoft.Data.SqlClient;

namespace SukiMars.Services;

public sealed class InventoryService
{
    public async Task<List<InventoryProduct>> GetProductsAsync(string? search = null)
    {
        const string query = """
            SELECT
                mi.itemID,
                mi.itemName,
                mi.itemCode,
                mi.itemCategory,
                mi.price,
                ISNULL(s.currentQty, 0) AS currentQty,
                -- sum qty sold in last 30 days
                ISNULL(
                    (
                        SELECT SUM(td.qty) 
                        FROM dbo.transaction_details td
                        INNER JOIN dbo.[transaction] t ON td.transactionID = t.transactionID
                        WHERE td.itemID = mi.itemID
                          AND t.transDateTime >= DATEADD(DAY, -30, GETDATE())
                    ), 0
                ) AS soldLast30Days
            FROM dbo.mart_items mi
            LEFT JOIN dbo.stock s ON s.itemID = mi.itemID
            WHERE (@search IS NULL OR @search = '' OR mi.itemName LIKE '%' + @search + '%' OR mi.itemCode LIKE '%' + @search + '%')
            ORDER BY mi.itemName ASC
            """;

        List<InventoryProduct> products = new();

        await using SqlConnection connection = new(DatabaseConfig.ConnectionString);
        await connection.OpenAsync();
        await using SqlCommand command = new(query, connection);
        command.Parameters.AddWithValue("@search", (object?)search ?? DBNull.Value);

        await using SqlDataReader reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            int itemId = reader.GetInt32(0);
            string itemName = reader.GetString(1);
            string itemCode = reader.GetString(2);
            string itemCategory = reader.IsDBNull(3) ? string.Empty : reader.GetString(3);
            decimal price = reader.GetDecimal(4);
            int currentQty = reader.IsDBNull(5) ? 0 : reader.GetInt32(5);
            int soldLast30Days = reader.IsDBNull(6) ? 0 : reader.GetInt32(6);

            // Calculate average daily usage over 30 days
            decimal avgDailyUsage = soldLast30Days / 30.0m;

            // Default parameters - can be moved to config or per-item columns later
            const int leadTimeDays = 7;
            const int safetyStock = 5;

            int reorderPoint = (int)Math.Ceiling(avgDailyUsage * leadTimeDays + safetyStock);

            // Determine level:
            // - Critical: currentQty == 0
            // - Warning: currentQty <= reorderPoint (and > 0)
            // - Good: currentQty > reorderPoint
            string reorderLevel;
            if (currentQty == 0)
            {
                reorderLevel = "Critical";
            }
            else if (reorderPoint > 0 && currentQty <= reorderPoint)
            {
                reorderLevel = "Warning";
            }
            else
            {
                reorderLevel = "Good";
            }

            products.Add(new InventoryProduct
            {
                ItemId = itemId,
                ItemName = itemName,
                ItemCode = itemCode,
                ItemCategory = itemCategory,
                Price = price,
                CurrentQty = currentQty,
                ReorderPoint = reorderPoint,
                ReorderLevel = reorderLevel
            });
        }

        return products;
    }

    public async Task<InventorySummary> GetSummaryAsync()
    {
        const string query = """
            SELECT
                COUNT(*) AS TotalProducts,
                SUM(CASE WHEN ISNULL(s.currentQty, 0) = 0 THEN 1 ELSE 0 END) AS OutOfStock,
                SUM(CASE WHEN ISNULL(s.currentQty, 0) BETWEEN 1 AND 12 THEN 1 ELSE 0 END) AS LowStock,
                COUNT(DISTINCT ISNULL(mi.itemCategory, '')) AS Categories
            FROM dbo.mart_items mi
            LEFT JOIN dbo.stock s ON s.itemID = mi.itemID
            """;

        await using SqlConnection connection = new(DatabaseConfig.ConnectionString);
        await connection.OpenAsync();
        await using SqlCommand command = new(query, connection);
        await using SqlDataReader reader = await command.ExecuteReaderAsync();

        if (!await reader.ReadAsync())
        {
            return new InventorySummary();
        }

        return new InventorySummary
        {
            TotalProducts = reader.IsDBNull(0) ? 0 : reader.GetInt32(0),
            OutOfStock = reader.IsDBNull(1) ? 0 : reader.GetInt32(1),
            LowStock = reader.IsDBNull(2) ? 0 : reader.GetInt32(2),
            Categories = reader.IsDBNull(3) ? 0 : reader.GetInt32(3)
        };
    }

    public async Task AddProductAsync(string itemName, string itemCode, string category, decimal price, int initialQty)
    {
        await using SqlConnection connection = new(DatabaseConfig.ConnectionString);
        await connection.OpenAsync();
        await using SqlTransaction transaction = connection.BeginTransaction();

        try
        {
            const string addItemSql = """
                INSERT INTO dbo.mart_items (itemName, itemCode, itemCategory, itemDescription, price)
                VALUES (@name, @code, @category, '', @price);
                SELECT CAST(SCOPE_IDENTITY() AS INT);
                """;

            await using SqlCommand addItem = new(addItemSql, connection, transaction);
            addItem.Parameters.AddWithValue("@name", itemName);
            addItem.Parameters.AddWithValue("@code", itemCode);
            addItem.Parameters.AddWithValue("@category", category);
            addItem.Parameters.AddWithValue("@price", price);
            int itemId = (int)(await addItem.ExecuteScalarAsync() ?? 0);

            const string addStockSql = """
                INSERT INTO dbo.stock (itemID, currentQty, lastUpdated)
                VALUES (@itemId, @qty, GETDATE());
                """;

            await using SqlCommand addStock = new(addStockSql, connection, transaction);
            addStock.Parameters.AddWithValue("@itemId", itemId);
            addStock.Parameters.AddWithValue("@qty", initialQty);
            await addStock.ExecuteNonQueryAsync();

            await transaction.CommitAsync();
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    public async Task UpdateProductAsync(int itemId, string itemName, string itemCode, string category, decimal price, int qty)
    {
        await using SqlConnection connection = new(DatabaseConfig.ConnectionString);
        await connection.OpenAsync();
        await using SqlTransaction transaction = connection.BeginTransaction();

        try
        {
            const string updateItemSql = """
                UPDATE dbo.mart_items
                SET itemName = @name,
                    itemCode = @code,
                    itemCategory = @category,
                    price = @price
                WHERE itemID = @itemId
                """;

            await using SqlCommand updateItem = new(updateItemSql, connection, transaction);
            updateItem.Parameters.AddWithValue("@name", itemName);
            updateItem.Parameters.AddWithValue("@code", itemCode);
            updateItem.Parameters.AddWithValue("@category", category);
            updateItem.Parameters.AddWithValue("@price", price);
            updateItem.Parameters.AddWithValue("@itemId", itemId);
            await updateItem.ExecuteNonQueryAsync();

            const string upsertStockSql = """
                MERGE dbo.stock AS target
                USING (SELECT @itemId AS itemID) AS src
                ON target.itemID = src.itemID
                WHEN MATCHED THEN
                    UPDATE SET currentQty = @qty, lastUpdated = GETDATE()
                WHEN NOT MATCHED THEN
                    INSERT (itemID, currentQty, lastUpdated)
                    VALUES (@itemId, @qty, GETDATE());
                """;

            await using SqlCommand upsertStock = new(upsertStockSql, connection, transaction);
            upsertStock.Parameters.AddWithValue("@itemId", itemId);
            upsertStock.Parameters.AddWithValue("@qty", qty);
            await upsertStock.ExecuteNonQueryAsync();

            await transaction.CommitAsync();
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    public async Task DeleteProductAsync(int itemId)
    {
        await using SqlConnection connection = new(DatabaseConfig.ConnectionString);
        await connection.OpenAsync();
        await using SqlTransaction transaction = connection.BeginTransaction();

        try
        {
            const string usedInSalesSql = """
                SELECT COUNT(1)
                FROM dbo.transaction_details
                WHERE itemID = @itemId
                """;

            await using (SqlCommand usedInSales = new(usedInSalesSql, connection, transaction))
            {
                usedInSales.Parameters.AddWithValue("@itemId", itemId);
                int salesReferences = (int)(await usedInSales.ExecuteScalarAsync() ?? 0);
                if (salesReferences > 0)
                {
                    throw new InvalidOperationException(
                        "Cannot delete this product because it already has sales history. ");
                }
            }

            // Safe to remove inventory detail references for unsold items.
            await using (SqlCommand deleteInventoryDetails = new("DELETE FROM dbo.inventory_details WHERE itemID = @itemId", connection, transaction))
            {
                deleteInventoryDetails.Parameters.AddWithValue("@itemId", itemId);
                await deleteInventoryDetails.ExecuteNonQueryAsync();
            }

            await using (SqlCommand deleteStock = new("DELETE FROM dbo.stock WHERE itemID = @itemId", connection, transaction))
            {
                deleteStock.Parameters.AddWithValue("@itemId", itemId);
                await deleteStock.ExecuteNonQueryAsync();
            }

            await using (SqlCommand deleteItem = new("DELETE FROM dbo.mart_items WHERE itemID = @itemId", connection, transaction))
            {
                deleteItem.Parameters.AddWithValue("@itemId", itemId);
                await deleteItem.ExecuteNonQueryAsync();
            }

            await transaction.CommitAsync();
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }
}

public sealed class InventoryProduct
{
    public int ItemId { get; init; }
    public string ItemName { get; init; } = string.Empty;
    public string ItemCode { get; init; } = string.Empty;
    public string ItemCategory { get; init; } = string.Empty;
    public decimal Price { get; init; }
    public int CurrentQty { get; init; }

    // New properties for ROP
    public int ReorderPoint { get; init; }
    public string ReorderLevel { get; init; } = "Good";

    public string Status =>
        CurrentQty == 0 ? "Out of Stock" :
        CurrentQty <= 12 ? "Low Stock" :
        "In Stock";
}

public sealed class InventorySummary
{
    public int TotalProducts { get; init; }
    public int LowStock { get; init; }
    public int OutOfStock { get; init; }
    public int Categories { get; init; }
}
