/*
    SukiMarsDB — Migration v2
    =========================
    Safe, idempotent. Run once on the live database.
    All steps use IF NOT EXISTS / IF EXISTS guards.

    Changes:
      1. mart_items        – add ProductType, ShelfLifeDays
      2. inventory_details – add orderedQuantity, receivedQuantity, UnitCost, ExpirationDate
                           - migrate quantity → orderedQuantity
                           - drop LotNumber, ManufacturingDate if they exist
                           - drop quantity after migration
      3. inventory         – drop acquisitionCost if it exists
      4. stock             – add inventoryDetailsID (nullable FK)
*/

USE SukiMarsDB;
GO

-- ═══════════════════════════════════════════════════════
-- 1. mart_items – ProductType, ShelfLifeDays
-- ═══════════════════════════════════════════════════════
IF NOT EXISTS (
    SELECT 1 FROM sys.columns
    WHERE object_id = OBJECT_ID('dbo.mart_items') AND name = 'ProductType')
    ALTER TABLE dbo.mart_items ADD ProductType NVARCHAR(20) NULL DEFAULT 'Non-Perishable';
GO

IF NOT EXISTS (
    SELECT 1 FROM sys.columns
    WHERE object_id = OBJECT_ID('dbo.mart_items') AND name = 'ShelfLifeDays')
    ALTER TABLE dbo.mart_items ADD ShelfLifeDays INT NULL;
GO

-- Default existing rows to Non-Perishable if null
UPDATE dbo.mart_items SET ProductType = 'Non-Perishable' WHERE ProductType IS NULL;
GO

-- ═══════════════════════════════════════════════════════
-- 2. inventory_details – new columns
-- ═══════════════════════════════════════════════════════

-- Add orderedQuantity
IF NOT EXISTS (
    SELECT 1 FROM sys.columns
    WHERE object_id = OBJECT_ID('dbo.inventory_details') AND name = 'orderedQuantity')
    ALTER TABLE dbo.inventory_details ADD orderedQuantity INT NULL;
GO

-- Add receivedQuantity
IF NOT EXISTS (
    SELECT 1 FROM sys.columns
    WHERE object_id = OBJECT_ID('dbo.inventory_details') AND name = 'receivedQuantity')
    ALTER TABLE dbo.inventory_details ADD receivedQuantity INT NULL DEFAULT 0;
GO

-- Add UnitCost
IF NOT EXISTS (
    SELECT 1 FROM sys.columns
    WHERE object_id = OBJECT_ID('dbo.inventory_details') AND name = 'UnitCost')
    ALTER TABLE dbo.inventory_details ADD UnitCost DECIMAL(10,2) NULL;
GO

-- Add ExpirationDate (if not already there from a previous session)
IF NOT EXISTS (
    SELECT 1 FROM sys.columns
    WHERE object_id = OBJECT_ID('dbo.inventory_details') AND name = 'ExpirationDate')
    ALTER TABLE dbo.inventory_details ADD ExpirationDate DATE NULL;
GO

-- Migrate: copy quantity → orderedQuantity for existing rows
IF EXISTS (
    SELECT 1 FROM sys.columns
    WHERE object_id = OBJECT_ID('dbo.inventory_details') AND name = 'quantity')
BEGIN
    UPDATE dbo.inventory_details
    SET orderedQuantity = quantity
    WHERE orderedQuantity IS NULL;
END
GO

-- Set receivedQuantity = orderedQuantity for already-received ASNs
UPDATE id
SET id.receivedQuantity = id.orderedQuantity
FROM dbo.inventory_details id
INNER JOIN dbo.inventory inv ON inv.inventoryID = id.inventoryID
WHERE inv.deliveryStatus = 'Received'
  AND id.receivedQuantity IS NULL OR id.receivedQuantity = 0;
GO

-- Drop LotNumber if it exists
IF EXISTS (
    SELECT 1 FROM sys.columns
    WHERE object_id = OBJECT_ID('dbo.inventory_details') AND name = 'LotNumber')
BEGIN
    -- Drop any index on LotNumber first
    DECLARE @ixLot NVARCHAR(200);
    SELECT @ixLot = i.name
    FROM sys.indexes i
    INNER JOIN sys.index_columns ic ON i.object_id = ic.object_id AND i.index_id = ic.index_id
    INNER JOIN sys.columns c ON ic.object_id = c.object_id AND ic.column_id = c.column_id
    WHERE i.object_id = OBJECT_ID('dbo.inventory_details') AND c.name = 'LotNumber';
    IF @ixLot IS NOT NULL
        EXEC('DROP INDEX [' + @ixLot + '] ON dbo.inventory_details');

    ALTER TABLE dbo.inventory_details DROP COLUMN LotNumber;
END
GO

-- Drop ManufacturingDate if it exists
IF EXISTS (
    SELECT 1 FROM sys.columns
    WHERE object_id = OBJECT_ID('dbo.inventory_details') AND name = 'ManufacturingDate')
    ALTER TABLE dbo.inventory_details DROP COLUMN ManufacturingDate;
GO

-- Drop quantity column now that orderedQuantity is populated
IF EXISTS (
    SELECT 1 FROM sys.columns
    WHERE object_id = OBJECT_ID('dbo.inventory_details') AND name = 'quantity')
BEGIN
    -- Drop any default constraint on quantity first
    DECLARE @dfQty NVARCHAR(200);
    SELECT @dfQty = dc.name
    FROM sys.default_constraints dc
    INNER JOIN sys.columns c ON dc.parent_object_id = c.object_id AND dc.parent_column_id = c.column_id
    WHERE c.object_id = OBJECT_ID('dbo.inventory_details') AND c.name = 'quantity';
    IF @dfQty IS NOT NULL
        EXEC('ALTER TABLE dbo.inventory_details DROP CONSTRAINT [' + @dfQty + ']');

    ALTER TABLE dbo.inventory_details DROP COLUMN quantity;
END
GO

-- ═══════════════════════════════════════════════════════
-- 3. inventory – drop acquisitionCost
-- ═══════════════════════════════════════════════════════
IF EXISTS (
    SELECT 1 FROM sys.columns
    WHERE object_id = OBJECT_ID('dbo.inventory') AND name = 'acquisitionCost')
BEGIN
    -- Drop any default constraint first
    DECLARE @dfAcq NVARCHAR(200);
    SELECT @dfAcq = dc.name
    FROM sys.default_constraints dc
    INNER JOIN sys.columns c ON dc.parent_object_id = c.object_id AND dc.parent_column_id = c.column_id
    WHERE c.object_id = OBJECT_ID('dbo.inventory') AND c.name = 'acquisitionCost';
    IF @dfAcq IS NOT NULL
        EXEC('ALTER TABLE dbo.inventory DROP CONSTRAINT [' + @dfAcq + ']');

    ALTER TABLE dbo.inventory DROP COLUMN acquisitionCost;
END
GO

-- ═══════════════════════════════════════════════════════
-- 4. stock – add inventoryDetailsID (nullable FK)
-- ═══════════════════════════════════════════════════════
IF NOT EXISTS (
    SELECT 1 FROM sys.columns
    WHERE object_id = OBJECT_ID('dbo.stock') AND name = 'inventoryDetailsID')
    ALTER TABLE dbo.stock ADD inventoryDetailsID INT NULL;
GO

-- Add FK if not yet there
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_stock_inventoryDetails')
    ALTER TABLE dbo.stock
        ADD CONSTRAINT FK_stock_inventoryDetails
        FOREIGN KEY (inventoryDetailsID) REFERENCES dbo.inventory_details(inventoryDetailsID);
GO

-- ═══════════════════════════════════════════════════════
-- 5. Repair existing stock rows for already-received ASNs
--    Link each stock row to its matching inventory_details row
-- ═══════════════════════════════════════════════════════
UPDATE s
SET s.inventoryDetailsID = (
    SELECT TOP 1 id.inventoryDetailsID
    FROM dbo.inventory_details id
    INNER JOIN dbo.inventory inv ON inv.inventoryID = id.inventoryID
    WHERE id.itemID = s.itemID
      AND inv.deliveryStatus = 'Received'
    ORDER BY id.inventoryDetailsID ASC
)
FROM dbo.stock s
WHERE s.inventoryDetailsID IS NULL;
GO

-- ═══════════════════════════════════════════════════════
-- 6. Verify
-- ═══════════════════════════════════════════════════════
SELECT 'mart_items columns' AS [Check], name FROM sys.columns WHERE object_id = OBJECT_ID('dbo.mart_items') AND name IN ('ProductType','ShelfLifeDays')
UNION ALL
SELECT 'inventory_details columns', name FROM sys.columns WHERE object_id = OBJECT_ID('dbo.inventory_details') AND name IN ('orderedQuantity','receivedQuantity','UnitCost','ExpirationDate')
UNION ALL
SELECT 'stock columns', name FROM sys.columns WHERE object_id = OBJECT_ID('dbo.stock') AND name = 'inventoryDetailsID';
GO
