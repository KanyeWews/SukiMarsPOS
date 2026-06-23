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
                ISNULL(mi.barcode, '') AS barcode,
                ISNULL(mi.itemDescription, '') AS itemDescription,
                mi.itemCategory,
                mi.price,
                ISNULL((SELECT SUM(currentQty) FROM dbo.stock WHERE itemID = mi.itemID), 0) AS currentQty,
                -- sum qty sold in last 30 days
                ISNULL(
                    (
                        SELECT SUM(td.qty) 
                        FROM dbo.transaction_details td
                        INNER JOIN dbo.[transaction] t ON td.transactionID = t.transactionID
                        WHERE td.itemID = mi.itemID
                          AND t.transDateTime >= DATEADD(DAY, -30, GETDATE())
                    ), 0
                ) AS soldLast30Days,
                ISNULL(mi.ProductType, 'Non-Perishable') AS ProductType,
                (
                    SELECT MIN(id.ExpirationDate)
                    FROM dbo.stock s
                    INNER JOIN dbo.inventory_details id ON id.inventoryDetailsID = s.inventoryDetailsID
                    WHERE s.itemID = mi.itemID AND s.currentQty > 0 AND id.ExpirationDate IS NOT NULL
                ) AS EarliestExpiration,
                mi.ShelfLifeDays
            FROM dbo.mart_items mi
            WHERE (@search IS NULL OR @search = '' OR mi.itemName LIKE '%' + @search + '%' OR mi.itemCode LIKE '%' + @search + '%' OR mi.barcode LIKE '%' + @search + '%')
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
            string barcode = reader.IsDBNull(3) ? string.Empty : reader.GetString(3);
            string itemDescription = reader.IsDBNull(4) ? string.Empty : reader.GetString(4);
            string itemCategory = reader.IsDBNull(5) ? string.Empty : reader.GetString(5);
            decimal price = reader.GetDecimal(6);
            int currentQty = reader.IsDBNull(7) ? 0 : reader.GetInt32(7);
            int soldLast30Days = reader.IsDBNull(8) ? 0 : reader.GetInt32(8);
            string productType = reader.IsDBNull(9) ? "Non-Perishable" : reader.GetString(9);
            DateTime? earliestExpiration = reader.IsDBNull(10) ? (DateTime?)null : reader.GetDateTime(10);
            int? shelfLifeDays = reader.IsDBNull(11) ? (int?)null : reader.GetInt32(11);

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
                Barcode = barcode,
                ItemDescription = itemDescription,
                ItemCategory = itemCategory,
                Price = price,
                CurrentQty = currentQty,
                ReorderPoint = reorderPoint,
                ReorderLevel = reorderLevel,
                ProductType = productType,
                EarliestExpiration = earliestExpiration,
                ShelfLifeDays = shelfLifeDays
            });
        }

        return products;
    }

    public async Task<InventorySummary> GetSummaryAsync()
    {
        const string query = """
            WITH ItemStock AS (
                SELECT 
                    mi.itemID,
                    mi.itemCategory,
                    ISNULL((SELECT SUM(currentQty) FROM dbo.stock WHERE itemID = mi.itemID), 0) AS totalQty
                FROM dbo.mart_items mi
            )
            SELECT
                COUNT(*) AS TotalProducts,
                SUM(CASE WHEN totalQty = 0 THEN 1 ELSE 0 END) AS OutOfStock,
                SUM(CASE WHEN totalQty BETWEEN 1 AND 12 THEN 1 ELSE 0 END) AS LowStock,
                COUNT(DISTINCT ISNULL(itemCategory, '')) AS Categories
            FROM ItemStock
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

    public async Task AddProductAsync(string itemName, string itemCode, string category, decimal price, int initialQty, string? barcode = null, string? description = null, string productType = "Non-Perishable", int? shelfLifeDays = null)
    {
        await using SqlConnection connection = new(DatabaseConfig.ConnectionString);
        await connection.OpenAsync();
        await using SqlTransaction transaction = connection.BeginTransaction();

        try
        {
            const string addItemSql = """
                INSERT INTO dbo.mart_items (itemName, itemCode, barcode, itemCategory, itemDescription, price, ProductType, ShelfLifeDays)
                VALUES (@name, @code, @barcode, @category, @description, @price, @productType, @shelfLifeDays);
                SELECT CAST(SCOPE_IDENTITY() AS INT);
                """;

            await using SqlCommand addItem = new(addItemSql, connection, transaction);
            addItem.Parameters.AddWithValue("@name", itemName);
            addItem.Parameters.AddWithValue("@code", itemCode);
            addItem.Parameters.AddWithValue("@barcode", string.IsNullOrWhiteSpace(barcode) ? DBNull.Value : barcode);
            addItem.Parameters.AddWithValue("@description", description ?? string.Empty);
            addItem.Parameters.AddWithValue("@category", category);
            addItem.Parameters.AddWithValue("@price", price);
            addItem.Parameters.AddWithValue("@productType", productType);
            addItem.Parameters.AddWithValue("@shelfLifeDays", shelfLifeDays.HasValue ? shelfLifeDays.Value : DBNull.Value);
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

    public async Task UpdateProductAsync(int itemId, string itemName, string itemCode, string category, decimal price, int qty, string? barcode = null, string? description = null, string productType = "Non-Perishable", int? shelfLifeDays = null)
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
                    barcode = @barcode,
                    itemDescription = @description,
                    itemCategory = @category,
                    price = @price,
                    ProductType = @productType,
                    ShelfLifeDays = @shelfLifeDays
                WHERE itemID = @itemId
                """;

            await using SqlCommand updateItem = new(updateItemSql, connection, transaction);
            updateItem.Parameters.AddWithValue("@name", itemName);
            updateItem.Parameters.AddWithValue("@code", itemCode);
            updateItem.Parameters.AddWithValue("@barcode", string.IsNullOrWhiteSpace(barcode) ? DBNull.Value : barcode);
            updateItem.Parameters.AddWithValue("@description", description ?? string.Empty);
            updateItem.Parameters.AddWithValue("@category", category);
            updateItem.Parameters.AddWithValue("@price", price);
            updateItem.Parameters.AddWithValue("@productType", productType);
            updateItem.Parameters.AddWithValue("@shelfLifeDays", shelfLifeDays.HasValue ? shelfLifeDays.Value : DBNull.Value);
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

    public async Task UpdateDescriptionAsync(int itemId, string description)
    {
        const string sql = "UPDATE dbo.mart_items SET itemDescription = @desc WHERE itemID = @itemId";

        await using SqlConnection connection = new(DatabaseConfig.ConnectionString);
        await connection.OpenAsync();
        await using SqlCommand cmd = new(sql, connection);
        cmd.Parameters.AddWithValue("@desc", description ?? string.Empty);
        cmd.Parameters.AddWithValue("@itemId", itemId);
        await cmd.ExecuteNonQueryAsync();
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

    // Returns perishable batches expiring within the next N days (not yet expired)
    public async Task<List<ExpiryBatchRow>> GetExpiringItemsAsync(int withinDays = 30)
    {
        const string sql = """
            SELECT
                mi.itemName,
                mi.itemCode,
                ISNULL(mi.itemCategory, '') AS itemCategory,
                idt.quantity,
                idt.ExpirationDate,
                DATEDIFF(DAY, GETDATE(), idt.ExpirationDate) AS daysLeft,
                ISNULL(inv.ASNNumber, '') AS ASNNumber,
                ISNULL(sup.supplierName, '') AS Supplier
            FROM dbo.inventory_details idt
            INNER JOIN dbo.mart_items mi ON mi.itemID = idt.itemID
            INNER JOIN dbo.inventory inv ON inv.inventoryID = idt.inventoryID
            LEFT JOIN dbo.supplier sup ON sup.supplierID = inv.supplierID
            WHERE mi.ProductType = 'Perishable'
              AND idt.ExpirationDate IS NOT NULL
              AND idt.ExpirationDate >= CAST(GETDATE() AS DATE)
              AND DATEDIFF(DAY, GETDATE(), idt.ExpirationDate) <= @withinDays
              AND inv.deliveryStatus = 'Received'
            ORDER BY idt.ExpirationDate ASC
            """;

        var list = new List<ExpiryBatchRow>();
        await using SqlConnection conn = new(DatabaseConfig.ConnectionString);
        await conn.OpenAsync();
        await using SqlCommand cmd = new(sql, conn);
        cmd.Parameters.AddWithValue("@withinDays", withinDays);
        await using SqlDataReader r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
        {
            list.Add(new ExpiryBatchRow
            {
                ItemName = r.IsDBNull(0) ? "" : r.GetString(0),
                ItemCode = r.IsDBNull(1) ? "" : r.GetString(1),
                Category = r.IsDBNull(2) ? "" : r.GetString(2),
                Quantity = r.IsDBNull(3) ? 0 : r.GetInt32(3),
                ExpirationDate = r.GetDateTime(4),
                DaysLeft = r.IsDBNull(5) ? 0 : r.GetInt32(5),
                ASNNumber = r.IsDBNull(6) ? "" : r.GetString(6),
                Supplier = r.IsDBNull(7) ? "" : r.GetString(7)
            });
        }
        return list;
    }

    // Returns perishable batches that have already expired
    public async Task<List<ExpiryBatchRow>> GetExpiredItemsAsync()
    {
        const string sql = """
            SELECT
                mi.itemName,
                mi.itemCode,
                ISNULL(mi.itemCategory, '') AS itemCategory,
                idt.quantity,
                idt.ExpirationDate,
                DATEDIFF(DAY, GETDATE(), idt.ExpirationDate) AS daysLeft,
                ISNULL(inv.ASNNumber, '') AS ASNNumber,
                ISNULL(sup.supplierName, '') AS Supplier
            FROM dbo.inventory_details idt
            INNER JOIN dbo.mart_items mi ON mi.itemID = idt.itemID
            INNER JOIN dbo.inventory inv ON inv.inventoryID = idt.inventoryID
            LEFT JOIN dbo.supplier sup ON sup.supplierID = inv.supplierID
            WHERE mi.ProductType = 'Perishable'
              AND idt.ExpirationDate IS NOT NULL
              AND idt.ExpirationDate < CAST(GETDATE() AS DATE)
              AND inv.deliveryStatus = 'Received'
            ORDER BY idt.ExpirationDate ASC
            """;

        var list = new List<ExpiryBatchRow>();
        await using SqlConnection conn = new(DatabaseConfig.ConnectionString);
        await conn.OpenAsync();
        await using SqlCommand cmd = new(sql, conn);
        await using SqlDataReader r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
        {
            list.Add(new ExpiryBatchRow
            {
                ItemName = r.IsDBNull(0) ? "" : r.GetString(0),
                ItemCode = r.IsDBNull(1) ? "" : r.GetString(1),
                Category = r.IsDBNull(2) ? "" : r.GetString(2),
                Quantity = r.IsDBNull(3) ? 0 : r.GetInt32(3),
                ExpirationDate = r.GetDateTime(4),
                DaysLeft = r.IsDBNull(5) ? 0 : r.GetInt32(5),
                ASNNumber = r.IsDBNull(6) ? "" : r.GetString(6),
                Supplier = r.IsDBNull(7) ? "" : r.GetString(7)
            });
        }
        return list;
    }
}


public sealed class InventoryProduct
{
    public int ItemId { get; init; }
    public string ItemName { get; init; } = string.Empty;
    public string ItemCode { get; init; } = string.Empty;
    public string Barcode { get; init; } = string.Empty;
    public string ItemDescription { get; init; } = string.Empty;
    public string ItemCategory { get; init; } = string.Empty;
    public decimal Price { get; init; }
    public int CurrentQty { get; init; }

    public int ReorderPoint { get; init; }
    public string ReorderLevel { get; init; } = "Good";

    public string ProductType { get; init; } = "Non-Perishable";
    public DateTime? EarliestExpiration { get; init; }
    public int? ShelfLifeDays { get; init; }

    public string ExpirationDateDisplay =>
        ProductType == "Perishable" && EarliestExpiration.HasValue 
            ? EarliestExpiration.Value.ToString("MMM dd, yyyy") 
            : "N/A";

    public int? RemainingShelfLifeDays =>
        ProductType == "Perishable" && EarliestExpiration.HasValue
            ? (int)(EarliestExpiration.Value.Date - DateTime.Now.Date).TotalDays
            : null;

    public string ShelfLifeDisplay =>
        ProductType == "Perishable"
            ? (RemainingShelfLifeDays.HasValue ? $"{RemainingShelfLifeDays} days" : "Unknown")
            : "N/A";

    public string ExpirationStatus
    {
        get
        {
            if (ProductType != "Perishable") return "N/A";
            if (!RemainingShelfLifeDays.HasValue) return "Unknown";
            int days = RemainingShelfLifeDays.Value;
            if (days <= 0) return "Expired";
            if (days <= 7) return "Critical";
            if (days <= 30) return "Warning";
            return "Fresh";
        }
    }

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

public sealed class ExpiryBatchRow
{
    public string ItemName { get; init; } = string.Empty;
    public string ItemCode { get; init; } = string.Empty;
    public string Category { get; init; } = string.Empty;
    public int Quantity { get; init; }
    public DateTime ExpirationDate { get; init; }
    public int DaysLeft { get; init; }
    public string ASNNumber { get; init; } = string.Empty;
    public string Supplier { get; init; } = string.Empty;

    public string DaysLeftDisplay => DaysLeft <= 0 ? "Expired" : $"{DaysLeft} day{(DaysLeft == 1 ? "" : "s")}";

    public string StatusLabel => DaysLeft <= 0 ? "Expired"
        : DaysLeft <= 7 ? "Critical"
        : DaysLeft <= 30 ? "Warning"
        : "OK";
}
