using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;

namespace SukiMars.Services
{
    public sealed class WarehouseService
    {
        // Reads basic shipment headers from dbo.inventory.
        public async Task<List<WarehouseShipment>> GetShipmentsAsync(string? search = null)
        {
            const string sql = """
                SELECT
                    inv.inventoryID,
                    ISNULL(inv.ASNNumber,
                        CONCAT('ASN-', YEAR(GETUTCDATE()), '-', RIGHT('0000' + CAST(inv.inventoryID AS VARCHAR(4)), 4))
                    ) AS ASN,
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
                ORDER BY inv.inventoryID DESC;
                """;

            var list = new List<WarehouseShipment>();
            await using SqlConnection conn = new(DatabaseConfig.ConnectionString);
            await conn.OpenAsync();
            await using SqlCommand cmd = new(sql, conn);
            cmd.Parameters.AddWithValue("@search", (object?)search ?? DBNull.Value);

            await using SqlDataReader reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                list.Add(new WarehouseShipment
                {
                    InventoryId = reader.GetInt32(0),
                    ASNNumber = reader.IsDBNull(1) ? string.Empty : reader.GetString(1),
                    Supplier = reader.IsDBNull(2) ? string.Empty : reader.GetString(2),
                    EstimatedDeliveryDate = reader.IsDBNull(3) ? DateTime.MinValue : reader.GetDateTime(3),
                    Status = reader.IsDBNull(4) ? string.Empty : reader.GetString(4),
                    LastUpdate = reader.IsDBNull(5) ? DateTime.MinValue : reader.GetDateTime(5),
                    Comments = reader.IsDBNull(6) ? string.Empty : reader.GetString(6),
                    UpdatedBy = reader.IsDBNull(7) ? string.Empty : reader.GetString(7)
                });
            }
            return list;
        }

        // Load all recorded suppliers
        public async Task<List<Supplier>> GetSuppliersAsync()
        {
            const string sql = "SELECT supplierID, supplierName, ISNULL(contact,'') FROM dbo.supplier ORDER BY supplierName;";
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
                    SupplierName = reader.IsDBNull(1) ? string.Empty : reader.GetString(1),
                    Contact = reader.IsDBNull(2) ? string.Empty : reader.GetString(2)
                });
            }
            return list;
        }

        // Find or create supplier by name — uses correct column name 'contact'
        public async Task<int> FindOrCreateSupplierAsync(SqlConnection conn, SqlTransaction tx, string supplierName, string? contact)
        {
            const string findSql = "SELECT supplierID FROM dbo.supplier WHERE supplierName = @name;";
            await using (SqlCommand find = new(findSql, conn, tx))
            {
                find.Parameters.AddWithValue("@name", supplierName);
                var result = await find.ExecuteScalarAsync();
                if (result != null && result != DBNull.Value)
                    return (int)result;
            }

            // Insert new supplier
            string insertSql = string.IsNullOrWhiteSpace(contact)
                ? "INSERT INTO dbo.supplier (supplierName) VALUES (@name); SELECT CAST(SCOPE_IDENTITY() AS INT);"
                : "INSERT INTO dbo.supplier (supplierName, contact) VALUES (@name, @contact); SELECT CAST(SCOPE_IDENTITY() AS INT);";

            await using SqlCommand insert = new(insertSql, conn, tx);
            insert.Parameters.AddWithValue("@name", supplierName);
            if (!string.IsNullOrWhiteSpace(contact))
                insert.Parameters.AddWithValue("@contact", contact);
            return (int)(await insert.ExecuteScalarAsync() ?? 0);
        }

        // Lookup products for SKU dropdown (includes ProductType for display)
        public async Task<List<ProductLookup>> GetProductsLookupAsync()
        {
            const string sql = """
                SELECT itemID, itemName, itemCode,
                       ISNULL(ProductType, 'Non-Perishable') AS ProductType
                FROM dbo.mart_items
                ORDER BY itemName;
                """;
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
                    ItemCode = reader.IsDBNull(2) ? string.Empty : reader.GetString(2),
                    ProductType = reader.IsDBNull(3) ? "Non-Perishable" : reader.GetString(3)
                });
            }
            return list;
        }

        public async Task<ProductLookup?> LookupProductByCodeAsync(string code)
        {
            const string sql = """
                SELECT itemID, itemName, itemCode,
                       ISNULL(ProductType,'Non-Perishable')
                FROM dbo.mart_items WHERE itemCode = @code;
                """;
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
                    ItemCode = reader.IsDBNull(2) ? string.Empty : reader.GetString(2),
                    ProductType = reader.IsDBNull(3) ? "Non-Perishable" : reader.GetString(3)
                };
            }
            return null;
        }

        public Task<ProductLookup?> LookupProductByCode(string code) => LookupProductByCodeAsync(code);

        // Create ASN shipment — uses orderedQuantity
        public async Task<int> CreateShipmentAsync(
            string supplierName, string? supplierContact, string? comments,
            DateTime estimatedDelivery, List<WarehouseShipmentDetail> details,
            int? userId, string? updatedByName = null)
        {
            await using SqlConnection conn = new(DatabaseConfig.ConnectionString);
            await conn.OpenAsync();
            await using var tx = conn.BeginTransaction();

            try
            {
                int supplierId = await FindOrCreateSupplierAsync(conn, tx, supplierName, supplierContact);

                // Insert inventory header (no acquisitionCost column any more)
                const string insertInvSql = @"
                    INSERT INTO dbo.inventory
                        (ASNNumber, acquisitionDate, deliveryDate, supplierID, userID, deliveryStatus, Comments, CreatedAt, UpdatedBy)
                    VALUES
                        (@asn, GETDATE(), @deliveryDate, @supplierID, @userID, 'Pending', @comments, GETDATE(), @updatedBy);
                    SELECT CAST(SCOPE_IDENTITY() AS INT);";

                int inventoryId;
                await using (SqlCommand insertInv = new(insertInvSql, conn, tx))
                {
                    insertInv.Parameters.AddWithValue("@asn", string.Empty);
                    insertInv.Parameters.AddWithValue("@deliveryDate", estimatedDelivery);
                    insertInv.Parameters.AddWithValue("@supplierID", (object)supplierId);
                    insertInv.Parameters.AddWithValue("@userID", userId.HasValue ? (object)userId.Value : DBNull.Value);
                    insertInv.Parameters.AddWithValue("@comments", string.IsNullOrWhiteSpace(comments) ? (object)DBNull.Value : comments);
                    insertInv.Parameters.AddWithValue("@updatedBy", string.IsNullOrWhiteSpace(updatedByName) ? DBNull.Value : updatedByName);
                    inventoryId = (int)(await insertInv.ExecuteScalarAsync() ?? 0);
                }

                // Back-fill the ASN number with the real ID
                string asn = $"ASN-{DateTime.UtcNow:yyyy}-{inventoryId:0000}";
                await using (SqlCommand updateAsn = new(
                    "UPDATE dbo.inventory SET ASNNumber = @asn WHERE inventoryID = @id;", conn, tx))
                {
                    updateAsn.Parameters.AddWithValue("@asn", asn);
                    updateAsn.Parameters.AddWithValue("@id", inventoryId);
                    await updateAsn.ExecuteNonQueryAsync();
                }

                // Insert detail rows using orderedQuantity
                const string insertDetailSql = @"
                    INSERT INTO dbo.inventory_details
                        (inventoryID, itemID, orderedQuantity, receivedQuantity)
                    VALUES
                        (@inventoryID, @itemID, @orderedQty, 0);";

                foreach (var d in details)
                {
                    await using SqlCommand insertDetail = new(insertDetailSql, conn, tx);
                    insertDetail.Parameters.AddWithValue("@inventoryID", inventoryId);
                    insertDetail.Parameters.AddWithValue("@itemID", d.ItemId);
                    insertDetail.Parameters.AddWithValue("@orderedQty", d.OrderedQuantity);
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

        // Simple status update (non-Received transitions)
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

        // Receive shipment — per-batch stock INSERT (FEFO-ready)
        public async Task ReceiveShipmentAsync(int inventoryId, string updatedBy,
            List<ReceiveBatchDetail>? batchDetails = null)
        {
            await using SqlConnection conn = new(DatabaseConfig.ConnectionString);
            await conn.OpenAsync();
            await using var tx = conn.BeginTransaction();

            try
            {
                // Mark ASN as Received
                await using (SqlCommand cmd = new(@"
                    UPDATE dbo.inventory
                    SET deliveryStatus = 'Received', UpdatedBy = @updatedBy
                    WHERE inventoryID = @id", conn, tx))
                {
                    cmd.Parameters.AddWithValue("@updatedBy", updatedBy);
                    cmd.Parameters.AddWithValue("@id", inventoryId);
                    await cmd.ExecuteNonQueryAsync();
                }

                // For each batch detail: update receivedQty + expiry, then INSERT a new stock row
                if (batchDetails != null)
                {
                    foreach (var batch in batchDetails)
                    {
                        // Update the inventory_details row
                        await using (SqlCommand cmd = new(@"
                            UPDATE dbo.inventory_details
                            SET receivedQuantity = @recvQty,
                                ExpirationDate   = @expDate,
                                UnitCost         = @unitCost
                            WHERE inventoryDetailsID = @detailId", conn, tx))
                        {
                            cmd.Parameters.AddWithValue("@recvQty", batch.ReceivedQuantity);
                            cmd.Parameters.AddWithValue("@expDate",
                                batch.ExpirationDate.HasValue ? (object)batch.ExpirationDate.Value : DBNull.Value);
                            cmd.Parameters.AddWithValue("@unitCost",
                                batch.UnitCost.HasValue ? (object)batch.UnitCost.Value : DBNull.Value);
                            cmd.Parameters.AddWithValue("@detailId", batch.InventoryDetailId);
                            await cmd.ExecuteNonQueryAsync();
                        }

                        // Insert a new per-batch stock row (FEFO)
                        await using (SqlCommand cmd = new(@"
                            INSERT INTO dbo.stock (inventoryDetailsID, itemID, currentQty, lastUpdated)
                            SELECT @detailId, itemID, @recvQty, GETDATE()
                            FROM dbo.inventory_details
                            WHERE inventoryDetailsID = @detailId", conn, tx))
                        {
                            cmd.Parameters.AddWithValue("@detailId", batch.InventoryDetailId);
                            cmd.Parameters.AddWithValue("@recvQty", batch.ReceivedQuantity);
                            await cmd.ExecuteNonQueryAsync();
                        }
                    }
                }
                else
                {
                    // No batch details provided — fall back: insert stock for every detail row
                    await using SqlCommand cmd = new(@"
                        INSERT INTO dbo.stock (inventoryDetailsID, itemID, currentQty, lastUpdated)
                        SELECT inventoryDetailsID, itemID, ISNULL(orderedQuantity, 0), GETDATE()
                        FROM dbo.inventory_details
                        WHERE inventoryID = @id", conn, tx);
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

        // Fetch all batch items for a shipment (view + receive modal)
        public async Task<List<ShipmentBatchItem>> GetShipmentItemsAsync(int inventoryId)
        {
            const string sql = """
                SELECT
                    idt.inventoryDetailsID,
                    idt.itemID,
                    mi.itemName,
                    mi.itemCode,
                    ISNULL(mi.ProductType, 'Non-Perishable') AS ProductType,
                    ISNULL(idt.orderedQuantity, 0)  AS orderedQuantity,
                    ISNULL(idt.receivedQuantity, 0) AS receivedQuantity,
                    idt.ExpirationDate,
                    ISNULL(idt.UnitCost, 0)         AS UnitCost
                FROM dbo.inventory_details idt
                INNER JOIN dbo.mart_items mi ON mi.itemID = idt.itemID
                WHERE idt.inventoryID = @id
                ORDER BY mi.itemName
                """;

            var list = new List<ShipmentBatchItem>();
            await using SqlConnection conn = new(DatabaseConfig.ConnectionString);
            await conn.OpenAsync();
            await using SqlCommand cmd = new(sql, conn);
            cmd.Parameters.AddWithValue("@id", inventoryId);
            await using SqlDataReader reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                list.Add(new ShipmentBatchItem
                {
                    InventoryDetailId = reader.GetInt32(0),
                    ItemId = reader.GetInt32(1),
                    ItemName = reader.IsDBNull(2) ? string.Empty : reader.GetString(2),
                    ItemCode = reader.IsDBNull(3) ? string.Empty : reader.GetString(3),
                    ProductType = reader.IsDBNull(4) ? "Non-Perishable" : reader.GetString(4),
                    OrderedQuantity = reader.GetInt32(5),
                    ReceivedQuantity = reader.GetInt32(6),
                    ExpirationDate = reader.IsDBNull(7) ? (DateTime?)null : reader.GetDateTime(7),
                    UnitCost = reader.IsDBNull(8) ? 0m : reader.GetDecimal(8)
                });
            }
            return list;
        }
    }

    // ─── Data models ────────────────────────────────────────────────────────────

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
        public bool CanEdit => !Status.Equals("Received", StringComparison.OrdinalIgnoreCase)
                            && !Status.Equals("Cancelled", StringComparison.OrdinalIgnoreCase);
    }

    public sealed class WarehouseShipmentDetail
    {
        public int ItemId { get; set; }
        public int OrderedQuantity { get; set; }
    }

    public sealed class ReceiveBatchDetail
    {
        public int InventoryDetailId { get; set; }
        public int ReceivedQuantity { get; set; }
        public DateTime? ExpirationDate { get; set; }
        public decimal? UnitCost { get; set; }
    }

    public sealed class ShipmentBatchItem
    {
        public int InventoryDetailId { get; set; }
        public int ItemId { get; set; }
        public string ItemName { get; set; } = string.Empty;
        public string ItemCode { get; set; } = string.Empty;
        public string ProductType { get; set; } = "Non-Perishable";
        public int OrderedQuantity { get; set; }
        public int ReceivedQuantity { get; set; }
        public DateTime? ExpirationDate { get; set; }
        public decimal UnitCost { get; set; }

        public bool IsPerishable => ProductType.Equals("Perishable", StringComparison.OrdinalIgnoreCase);

        public string ExpirationDisplay =>
            ExpirationDate.HasValue
                ? ExpirationDate.Value.ToString("MMM dd, yyyy")
                : (IsPerishable ? "Not set" : "N/A");

        public int? DaysLeft =>
            ExpirationDate.HasValue
                ? (int)(ExpirationDate.Value.Date - DateTime.Now.Date).TotalDays
                : null;

        public string ShelfLifeStatus
        {
            get
            {
                if (!IsPerishable) return "N/A";
                if (!DaysLeft.HasValue) return "—";
                int d = DaysLeft.Value;
                if (d <= 0)  return "Expired";
                if (d <= 7)  return "Critical";
                if (d <= 30) return "Warning";
                return "Fresh";
            }
        }
    }

    public sealed class Supplier
    {
        public int SupplierID { get; set; }
        public string SupplierName { get; set; } = string.Empty;
        public string Contact { get; set; } = string.Empty;
    }

    public sealed class ProductLookup
    {
        public int ItemId { get; set; }
        public string ItemName { get; set; } = string.Empty;
        public string ItemCode { get; set; } = string.Empty;
        public string ProductType { get; set; } = "Non-Perishable";
    }
}
