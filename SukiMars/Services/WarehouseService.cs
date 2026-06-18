using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;

namespace SukiMars.Services
{
    public sealed class WarehouseService
    {
        // Reads basic shipment headers from dbo.inventory.
        // Adjust column names if your schema differs.
        public async Task<List<WarehouseShipment>> GetShipmentsAsync(string? search = null)
        {
            const string sql = """
                SELECT
                    inv.inventoryID,
                    CONCAT('ASN-', YEAR(GETUTCDATE()), '-', RIGHT('0000' + CAST(inv.inventoryID AS VARCHAR(4)), 4)) AS ASN,
                    ISNULL(sup.supplierName, '') AS Supplier,
                    inv.deliveryDate,
                    ISNULL(inv.deliveryStatus, '') AS Status,
                    ISNULL(inv.deliveryDate, inv.acquisitionDate) AS LastUpdate,
                    ISNULL(inv.Comments, '') AS Comments,
                    ISNULL(LTRIM(RTRIM(u.firstName + ' ' + u.lastName)), ISNULL(inv.UpdatedBy, '')) AS UpdatedBy
                FROM dbo.inventory inv
                LEFT JOIN dbo.supplier sup ON sup.supplierID = inv.supplierID
                LEFT JOIN dbo.user_accounts u ON u.userID = inv.userID
                WHERE (@search IS NULL OR @search = ''
                      OR CAST(inv.inventoryID AS VARCHAR(20)) LIKE '%' + @search + '%'
                      OR sup.supplierName LIKE '%' + @search + '%')
                ORDER BY inv.deliveryDate DESC;
                """;

            var list = new List<WarehouseShipment>();

            await using SqlConnection conn = new(DatabaseConfig.ConnectionString);
            await conn.OpenAsync();
            await using SqlCommand cmd = new(sql, conn);
            cmd.Parameters.AddWithValue("@search", (object?)search ?? DBNull.Value);

            await using SqlDataReader reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var s = new WarehouseShipment
                {
                    InventoryId = reader.GetInt32(0),
                    ASNNumber = reader.IsDBNull(1) ? string.Empty : reader.GetString(1),
                    Supplier = reader.IsDBNull(2) ? string.Empty : reader.GetString(2),
                    EstimatedDeliveryDate = reader.IsDBNull(3) ? DateTime.MinValue : reader.GetDateTime(3),
                    Status = reader.IsDBNull(4) ? string.Empty : reader.GetString(4),
                    LastUpdate = reader.IsDBNull(5) ? DateTime.MinValue : reader.GetDateTime(5),
                    Comments = reader.IsDBNull(6) ? string.Empty : reader.GetString(6),
                    UpdatedBy = reader.IsDBNull(7) ? string.Empty : reader.GetString(7)
                };

                list.Add(s);
            }

            return list;
        }

        // Load suppliers
        public async Task<List<Supplier>> GetSuppliersAsync()
        {
            const string sql = "SELECT supplierID, supplierName FROM dbo.supplier ORDER BY supplierName;";
            var list = new List<Supplier>();
            await using SqlConnection conn = new(DatabaseConfig.ConnectionString);
            await conn.OpenAsync();
            await using SqlCommand cmd = new(sql, conn);
            await using SqlDataReader reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                list.Add(new Supplier
                {
                    SupplierID = reader.IsDBNull(0) ? 0 : reader.GetInt32(0),
                    SupplierName = reader.IsDBNull(1) ? string.Empty : reader.GetString(1)
                });
            }
            return list;
        }

        // Lookup products for SKU dropdown
        public async Task<List<ProductLookup>> GetProductsLookupAsync()
        {
            const string sql = "SELECT itemID, itemName, itemCode FROM dbo.mart_items ORDER BY itemName;";
            var list = new List<ProductLookup>();
            await using SqlConnection conn = new(DatabaseConfig.ConnectionString);
            await conn.OpenAsync();
            await using SqlCommand cmd = new(sql, conn);
            await using SqlDataReader reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                list.Add(new ProductLookup
                {
                    ItemId = reader.IsDBNull(0) ? 0 : reader.GetInt32(0),
                    ItemName = reader.IsDBNull(1) ? string.Empty : reader.GetString(1),
                    ItemCode = reader.IsDBNull(2) ? string.Empty : reader.GetString(2)
                });
            }
            return list;
        }

        // helper to lookup single product by code (sync-friendly small helper)
        public async Task<ProductLookup?> LookupProductByCodeAsync(string code)
        {
            const string sql = "SELECT itemID, itemName, itemCode FROM dbo.mart_items WHERE itemCode = @code;";
            await using SqlConnection conn = new(DatabaseConfig.ConnectionString);
            await conn.OpenAsync();
            await using SqlCommand cmd = new(sql, conn);
            cmd.Parameters.AddWithValue("@code", code);
            await using SqlDataReader reader = await cmd.ExecuteReaderAsync();
            if (await reader.ReadAsync())
            {
                return new ProductLookup
                {
                    ItemId = reader.IsDBNull(0) ? 0 : reader.GetInt32(0),
                    ItemName = reader.IsDBNull(1) ? string.Empty : reader.GetString(1),
                    ItemCode = reader.IsDBNull(2) ? string.Empty : reader.GetString(2)
                };
            }
            return null;
        }

        // Synchronous wrapper used by UI helper (keeps UI code simple)
        public Task<ProductLookup?> LookupProductByCode(string code) => LookupProductByCodeAsync(code);

        // Create shipment (inventory header + details). Returns created inventoryID.
        public async Task<int> CreateShipmentAsync(int? supplierId, string? comments, DateTime estimatedDelivery, List<WarehouseShipmentDetail> details, int? userId, string? updatedByName = null)
        {
            await using SqlConnection conn = new(DatabaseConfig.ConnectionString);
            await conn.OpenAsync();
            await using var tx = conn.BeginTransaction();

            try
            {
                const string insertInvSql = @"
                    INSERT INTO dbo.inventory (ASNNumber, acquisitionDate, deliveryDate, supplierID, acquisitionCost, userID, deliveryStatus, Comments, CreatedAt, UpdatedBy)
                    VALUES (@asn, GETDATE(), @deliveryDate, @supplierID, 0.0, @userID, 'Pending', @comments, GETDATE(), @updatedBy);
                    SELECT CAST(SCOPE_IDENTITY() AS INT);
                ";

                await using SqlCommand insertInv = new(insertInvSql, conn, tx);
                string tempAsn = ""; // will update
                insertInv.Parameters.AddWithValue("@asn", tempAsn);
                insertInv.Parameters.AddWithValue("@deliveryDate", estimatedDelivery);
                insertInv.Parameters.AddWithValue("@supplierID", supplierId.HasValue ? (object)supplierId.Value : DBNull.Value);
                insertInv.Parameters.AddWithValue("@userID", userId.HasValue ? (object)userId.Value : DBNull.Value);
                insertInv.Parameters.AddWithValue("@comments", string.IsNullOrWhiteSpace(comments) ? (object)DBNull.Value : comments);
                insertInv.Parameters.AddWithValue("@updatedBy", string.IsNullOrWhiteSpace(updatedByName) ? DBNull.Value : updatedByName);

                int inventoryId = (int)(await insertInv.ExecuteScalarAsync() ?? 0);

                string asn = $"ASN-{DateTime.UtcNow:yyyy}-{inventoryId:0000}";

                const string updateAsnSql = "UPDATE dbo.inventory SET ASNNumber = @asn WHERE inventoryID = @inventoryId;";
                await using SqlCommand updateAsn = new(updateAsnSql, conn, tx);
                updateAsn.Parameters.AddWithValue("@asn", asn);
                updateAsn.Parameters.AddWithValue("@inventoryId", inventoryId);
                await updateAsn.ExecuteNonQueryAsync();

                const string insertDetailSql = @"
                    INSERT INTO dbo.inventory_details (inventoryID, itemID, quantity)
                    VALUES (@inventoryID, @itemID, @quantity);
                ";

                foreach (var d in details)
                {
                    await using SqlCommand insertDetail = new(insertDetailSql, conn, tx);
                    insertDetail.Parameters.AddWithValue("@inventoryID", inventoryId);
                    insertDetail.Parameters.AddWithValue("@itemID", d.ItemId);
                    insertDetail.Parameters.AddWithValue("@quantity", d.Quantity);
                    await insertDetail.ExecuteNonQueryAsync();
                }

                await tx.CommitAsync();
                return inventoryId;
            }
            catch
            {
                await tx.RollbackAsync();
                throw;
            }
        }

        public async Task UpdateShipmentStatusAsync(int inventoryId, string newStatus, string updatedBy)
        {
            const string sql = @"
                UPDATE dbo.inventory
                SET deliveryStatus = @status, UpdatedBy = @updatedBy
                WHERE inventoryID = @id";

            await using SqlConnection conn = new(DatabaseConfig.ConnectionString);
            await conn.OpenAsync();
            await using SqlCommand cmd = new(sql, conn);
            cmd.Parameters.AddWithValue("@status", newStatus);
            cmd.Parameters.AddWithValue("@updatedBy", updatedBy);
            cmd.Parameters.AddWithValue("@id", inventoryId);
            await cmd.ExecuteNonQueryAsync();
        }

        public async Task ReceiveShipmentAsync(int inventoryId, string updatedBy)
        {
            await using SqlConnection conn = new(DatabaseConfig.ConnectionString);
            await conn.OpenAsync();
            await using var tx = conn.BeginTransaction();

            try
            {
                const string updateStatusSql = @"
                    UPDATE dbo.inventory
                    SET deliveryStatus = 'Delivered', UpdatedBy = @updatedBy
                    WHERE inventoryID = @id";

                await using (SqlCommand cmd = new(updateStatusSql, conn, tx))
                {
                    cmd.Parameters.AddWithValue("@updatedBy", updatedBy);
                    cmd.Parameters.AddWithValue("@id", inventoryId);
                    await cmd.ExecuteNonQueryAsync();
                }

                const string upsertStockSql = @"
                    MERGE dbo.stock AS target
                    USING (SELECT itemID, quantity FROM dbo.inventory_details WHERE inventoryID = @id) AS src
                    ON target.itemID = src.itemID
                    WHEN MATCHED THEN
                        UPDATE SET currentQty = target.currentQty + src.quantity, lastUpdated = GETDATE()
                    WHEN NOT MATCHED THEN
                        INSERT (itemID, currentQty, lastUpdated)
                        VALUES (src.itemID, src.quantity, GETDATE());";

                await using (SqlCommand cmd = new(upsertStockSql, conn, tx))
                {
                    cmd.Parameters.AddWithValue("@id", inventoryId);
                    await cmd.ExecuteNonQueryAsync();
                }

                await tx.CommitAsync();
            }
            catch
            {
                await tx.RollbackAsync();
                throw;
            }
        }

        // Returns processed order items (join inventory + details + product + supplier)
        public async Task<List<ProcessedOrderItem>> GetProcessedOrderItemsAsync()
        {
            const string sql = @"
                SELECT
                    CONCAT('ASN-', YEAR(GETUTCDATE()), '-', RIGHT('0000' + CAST(inv.inventoryID AS VARCHAR(4)), 4)) AS ASN,
                    ISNULL(mi.itemName, '') AS ItemName,
                    ISNULL(mi.itemCode, '') AS ItemCode,
                    id.quantity,
                    ISNULL(sup.supplierName, '') AS Supplier,
                    inv.deliveryDate AS EstimatedDeliveryDate
                FROM dbo.inventory inv
                INNER JOIN dbo.inventory_details id ON id.inventoryID = inv.inventoryID
                LEFT JOIN dbo.mart_items mi ON mi.itemID = id.itemID
                LEFT JOIN dbo.supplier sup ON sup.supplierID = inv.supplierID
                ORDER BY inv.deliveryDate DESC;
            ";

            var result = new List<ProcessedOrderItem>();
            await using SqlConnection conn = new(DatabaseConfig.ConnectionString);
            await conn.OpenAsync();
            await using SqlCommand cmd = new(sql, conn);
            await using SqlDataReader r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync())
            {
                result.Add(new ProcessedOrderItem
                {
                    ASNNumber = r.IsDBNull(0) ? string.Empty : r.GetString(0),
                    ItemName = r.IsDBNull(1) ? string.Empty : r.GetString(1),
                    ItemCode = r.IsDBNull(2) ? string.Empty : r.GetString(2),
                    Quantity = r.IsDBNull(3) ? 0 : r.GetInt32(3),
                    ShelfLife = 0,
                    ExpiryDate = null,
                    Supplier = r.IsDBNull(4) ? string.Empty : r.GetString(4),
                    Comments = string.Empty,
                    EstimatedDeliveryDate = r.IsDBNull(5) ? (DateTime?)null : r.GetDateTime(5)
                });
            }

            return result;
        }
    }

    public sealed class WarehouseShipment
    {
        public int InventoryId { get; set; }
        public string ASNNumber { get; set; } = string.Empty;
        public string Supplier { get; set; } = string.Empty;
        public string? Comments { get; set; }
        public DateTime EstimatedDeliveryDate { get; set; }
        public string Status { get; set; } = string.Empty;
        public DateTime LastUpdate { get; set; }
        public string UpdatedBy { get; set; } = string.Empty;
        public bool CanEdit => !Status.Equals("Delivered", StringComparison.OrdinalIgnoreCase)
                            && !Status.Equals("Cancelled", StringComparison.OrdinalIgnoreCase);
    }

    public sealed class WarehouseShipmentDetail
    {
        public int ItemId { get; set; }
        public int Quantity { get; set; }
    }

    public sealed class Supplier
    {
        public int SupplierID { get; set; }
        public string SupplierName { get; set; } = string.Empty;
    }

    public sealed class ProductLookup
    {
        public int ItemId { get; set; }
        public string ItemName { get; set; } = string.Empty;
        public string ItemCode { get; set; } = string.Empty;
    }

    public sealed class ProcessedOrderItem
    {
        public string ASNNumber { get; set; } = string.Empty;
        public string ItemName { get; set; } = string.Empty;
        public string ItemCode { get; set; } = string.Empty;
        public int Quantity { get; set; }
        public int ShelfLife { get; set; }
        public DateTime? ExpiryDate { get; set; }
        public string Supplier { get; set; } = string.Empty;
        public string Comments { get; set; } = string.Empty;
        public DateTime? EstimatedDeliveryDate { get; set; }
    }
}
