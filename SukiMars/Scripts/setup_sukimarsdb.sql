/*
    SukiMarsDB — Full Database Setup Script
    ========================================
    Creates the database, all tables, foreign keys, and seed data.
    Safe to re-run: uses IF NOT EXISTS checks throughout.

    Usage:
        sqlcmd -S YOUR_SERVER -i setup_sukimarsdb.sql
*/

-- 1) Create database
IF DB_ID('SukiMarsDB') IS NULL
    CREATE DATABASE SukiMarsDB;
GO

USE SukiMarsDB;
GO

-- =========================================
-- 2) Tables (dependency order)
-- =========================================

-- user_accounts
IF OBJECT_ID('dbo.user_accounts', 'U') IS NULL
CREATE TABLE dbo.user_accounts (
    userID       INT           IDENTITY(1,1) NOT NULL PRIMARY KEY,
    firstName    VARCHAR(100)  NULL,
    lastName     VARCHAR(100)  NULL,
    username     VARCHAR(100)  NOT NULL,
    [password]   VARCHAR(255)  NOT NULL,
    pincode      VARCHAR(20)   NULL,
    userRole     VARCHAR(50)   NOT NULL,
    [status]     VARCHAR(50)   NOT NULL
);
GO

-- supplier
IF OBJECT_ID('dbo.supplier', 'U') IS NULL
CREATE TABLE dbo.supplier (
    supplierID   INT           IDENTITY(1,1) NOT NULL PRIMARY KEY,
    supplierName VARCHAR(150)  NULL,
    contact      VARCHAR(150)  NULL
);
GO

-- mart_items
IF OBJECT_ID('dbo.mart_items', 'U') IS NULL
CREATE TABLE dbo.mart_items (
    itemID          INT            IDENTITY(1,1) NOT NULL PRIMARY KEY,
    itemName        VARCHAR(150)   NULL,
    itemCode        VARCHAR(100)   NULL,
    itemCategory    VARCHAR(100)   NULL,
    itemDescription VARCHAR(MAX)   NULL,
    price           DECIMAL(10,2)  NOT NULL,
    barcode         VARCHAR(50)    NULL
);
GO

-- stock
IF OBJECT_ID('dbo.stock', 'U') IS NULL
CREATE TABLE dbo.stock (
    stockID     INT      IDENTITY(1,1) NOT NULL PRIMARY KEY,
    itemID      INT      NOT NULL,
    currentQty  INT      NOT NULL DEFAULT 0,
    lastUpdated DATETIME NULL
);
GO

-- inventory (warehouse shipment headers)
IF OBJECT_ID('dbo.inventory', 'U') IS NULL
CREATE TABLE dbo.inventory (
    inventoryID     INT            IDENTITY(1,1) NOT NULL PRIMARY KEY,
    acquisitionDate DATE           NULL,
    deliveryDate    DATE           NULL,
    supplierID      INT            NULL,
    acquisitionCost DECIMAL(10,2)  NULL,
    userID          INT            NULL,
    deliveryStatus  VARCHAR(50)    NULL,
    ASNNumber       VARCHAR(50)    NULL,
    Comments        VARCHAR(500)   NULL,
    CreatedAt       DATETIME       NULL DEFAULT GETDATE(),
    UpdatedBy       VARCHAR(100)   NULL
);
GO

-- inventory_details (warehouse shipment line items)
IF OBJECT_ID('dbo.inventory_details', 'U') IS NULL
CREATE TABLE dbo.inventory_details (
    inventoryDetailsID INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
    inventoryID        INT NOT NULL,
    itemID             INT NOT NULL,
    quantity           INT NOT NULL
);
GO

-- transaction
IF OBJECT_ID('dbo.[transaction]', 'U') IS NULL
CREATE TABLE dbo.[transaction] (
    transactionID   INT            IDENTITY(1,1) NOT NULL PRIMARY KEY,
    transDateTime   DATETIME       NULL,
    discountAmount  DECIMAL(10,2)  NULL,
    totalItem       INT            NULL,
    totalAmount     DECIMAL(10,2)  NULL,
    VATableSales    DECIMAL(10,2)  NULL,
    VAT             DECIMAL(10,2)  NULL,
    invoiceNumber   VARCHAR(100)   NULL,
    paymentMethod   VARCHAR(50)    NULL,
    userID          INT            NULL,
    referenceNumber VARCHAR(100)   NULL
);
GO

-- transaction_details
IF OBJECT_ID('dbo.transaction_details', 'U') IS NULL
CREATE TABLE dbo.transaction_details (
    transactionDetailsID INT           IDENTITY(1,1) NOT NULL PRIMARY KEY,
    transactionID        INT           NOT NULL,
    itemID               INT           NOT NULL,
    qty                  INT           NOT NULL,
    unitPrice            DECIMAL(10,2) NOT NULL,
    total                DECIMAL(10,2) NOT NULL
);
GO

-- =========================================
-- 3) Foreign Keys
-- =========================================

IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_stock_item')
    ALTER TABLE dbo.stock
        ADD CONSTRAINT FK_stock_item FOREIGN KEY (itemID) REFERENCES dbo.mart_items(itemID);
GO

IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_inventory_supplier')
    ALTER TABLE dbo.inventory
        ADD CONSTRAINT FK_inventory_supplier FOREIGN KEY (supplierID) REFERENCES dbo.supplier(supplierID);
GO

IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_inventory_user_accounts')
    ALTER TABLE dbo.inventory
        ADD CONSTRAINT FK_inventory_user_accounts FOREIGN KEY (userID) REFERENCES dbo.user_accounts(userID);
GO

IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_inventory_details_inventory')
    ALTER TABLE dbo.inventory_details
        ADD CONSTRAINT FK_inventory_details_inventory FOREIGN KEY (inventoryID) REFERENCES dbo.inventory(inventoryID);
GO

IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_inventory_details_item')
    ALTER TABLE dbo.inventory_details
        ADD CONSTRAINT FK_inventory_details_item FOREIGN KEY (itemID) REFERENCES dbo.mart_items(itemID);
GO

IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_transaction_user_accounts')
    ALTER TABLE dbo.[transaction]
        ADD CONSTRAINT FK_transaction_user_accounts FOREIGN KEY (userID) REFERENCES dbo.user_accounts(userID);
GO

IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_transaction_details_transaction')
    ALTER TABLE dbo.transaction_details
        ADD CONSTRAINT FK_transaction_details_transaction FOREIGN KEY (transactionID) REFERENCES dbo.[transaction](transactionID);
GO

IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_transaction_details_item')
    ALTER TABLE dbo.transaction_details
        ADD CONSTRAINT FK_transaction_details_item FOREIGN KEY (itemID) REFERENCES dbo.mart_items(itemID);
GO

-- =========================================
-- 4) Seed Data
-- =========================================

-- Users
IF NOT EXISTS (SELECT 1 FROM dbo.user_accounts WHERE username = 'cashier1')
    INSERT INTO dbo.user_accounts (firstName, lastName, username, [password], pincode, userRole, [status])
    VALUES ('Cashier', 'One', 'cashier1', '1234', '1111', 'Cashier', 'Active');

IF NOT EXISTS (SELECT 1 FROM dbo.user_accounts WHERE username = 'manager1')
    INSERT INTO dbo.user_accounts (firstName, lastName, username, [password], pincode, userRole, [status])
    VALUES ('Manager', 'One', 'manager1', '1234', '2222', 'Manager', 'Active');

IF NOT EXISTS (SELECT 1 FROM dbo.user_accounts WHERE username = 'admin1')
    INSERT INTO dbo.user_accounts (firstName, lastName, username, [password], pincode, userRole, [status])
    VALUES ('Admin', 'One', 'admin1', '1234', '3333', 'Admin', 'Active');
GO

-- Suppliers
IF NOT EXISTS (SELECT 1 FROM dbo.supplier WHERE supplierName = 'Fresh Source Trading')
    INSERT INTO dbo.supplier (supplierName, contact) VALUES ('Fresh Source Trading', '0917-111-1001');

IF NOT EXISTS (SELECT 1 FROM dbo.supplier WHERE supplierName = 'Metro Canned Depot')
    INSERT INTO dbo.supplier (supplierName, contact) VALUES ('Metro Canned Depot', '0917-111-1002');

IF NOT EXISTS (SELECT 1 FROM dbo.supplier WHERE supplierName = 'Luzon Dairy Distribution')
    INSERT INTO dbo.supplier (supplierName, contact) VALUES ('Luzon Dairy Distribution', '0917-111-1003');
GO

-- Mart Items
IF NOT EXISTS (SELECT 1 FROM dbo.mart_items WHERE itemCode = 'BEV-COKE-1L')
    INSERT INTO dbo.mart_items (itemName, itemCode, itemCategory, itemDescription, price)
    VALUES ('Coke 1L', 'BEV-COKE-1L', 'Beverages', 'Carbonated drink 1 liter', 55.00);

IF NOT EXISTS (SELECT 1 FROM dbo.mart_items WHERE itemCode = 'BEV-SPRT-1L')
    INSERT INTO dbo.mart_items (itemName, itemCode, itemCategory, itemDescription, price)
    VALUES ('Sprite 1L', 'BEV-SPRT-1L', 'Beverages', 'Lemon-lime soda 1 liter', 55.00);

IF NOT EXISTS (SELECT 1 FROM dbo.mart_items WHERE itemCode = 'CAN-SRD-155')
    INSERT INTO dbo.mart_items (itemName, itemCode, itemCategory, itemDescription, price)
    VALUES ('Sardines 155g', 'CAN-SRD-155', 'Canned Goods', 'Tomato sardines 155g', 28.00);

IF NOT EXISTS (SELECT 1 FROM dbo.mart_items WHERE itemCode = 'CAN-CRND-150')
    INSERT INTO dbo.mart_items (itemName, itemCode, itemCategory, itemDescription, price)
    VALUES ('Corned Beef 150g', 'CAN-CRND-150', 'Canned Goods', 'Premium corned beef 150g', 65.00);

IF NOT EXISTS (SELECT 1 FROM dbo.mart_items WHERE itemCode = 'DAI-MLK-1L')
    INSERT INTO dbo.mart_items (itemName, itemCode, itemCategory, itemDescription, price)
    VALUES ('Fresh Milk 1L', 'DAI-MLK-1L', 'Dairy', 'Fresh milk 1 liter', 78.00);

IF NOT EXISTS (SELECT 1 FROM dbo.mart_items WHERE itemCode = 'PRO-BANA-1K')
    INSERT INTO dbo.mart_items (itemName, itemCode, itemCategory, itemDescription, price)
    VALUES ('Banana 1kg', 'PRO-BANA-1K', 'Produce', 'Lakatan banana 1 kilogram', 90.00);

IF NOT EXISTS (SELECT 1 FROM dbo.mart_items WHERE itemCode = 'BEV-WTR-1L')
    INSERT INTO dbo.mart_items (itemName, itemCode, itemCategory, itemDescription, price)
    VALUES ('Water 1L', 'BEV-WTR-1L', 'Beverages', 'Purified drinking water 1 liter', 20.00);

IF NOT EXISTS (SELECT 1 FROM dbo.mart_items WHERE itemCode = 'PRO-ON-100')
    INSERT INTO dbo.mart_items (itemName, itemCode, itemCategory, itemDescription, price)
    VALUES ('Onion 100g', 'PRO-ON-100', 'Produce', 'Fresh red onion 100 grams', 30.00);

IF NOT EXISTS (SELECT 1 FROM dbo.mart_items WHERE itemCode = 'DAI-CHS-1')
    INSERT INTO dbo.mart_items (itemName, itemCode, itemCategory, itemDescription, price)
    VALUES ('Quick Melt Cheese', 'DAI-CHS-1', 'Dairy', 'Quick melt cheese for cooking and sandwiches', 20.00);

IF NOT EXISTS (SELECT 1 FROM dbo.mart_items WHERE itemCode = 'MEAT-CHKN-1')
    INSERT INTO dbo.mart_items (itemName, itemCode, itemCategory, itemDescription, price)
    VALUES ('Whole Chicken', 'MEAT-CHKN-1', 'Meat', 'Whole dressed chicken, fresh and ready to cook', 189.00);

IF NOT EXISTS (SELECT 1 FROM dbo.mart_items WHERE itemCode = 'MEAT-BF-500g')
    INSERT INTO dbo.mart_items (itemName, itemCode, itemCategory, itemDescription, price)
    VALUES ('Ground Beef 500g', 'MEAT-BF-500g', 'Meat', 'Lean ground beef 500 grams, ideal for burgers and sauces', 289.00);

IF NOT EXISTS (SELECT 1 FROM dbo.mart_items WHERE itemCode = 'MEAT-PRK-500g')
    INSERT INTO dbo.mart_items (itemName, itemCode, itemCategory, itemDescription, price)
    VALUES ('Pork Chop 500g', 'MEAT-PRK-500g', 'Meat', 'Bone-in pork chop 500 grams, great for grilling', 169.00);
GO

-- Stock (MERGE upsert)
;WITH src AS (
    SELECT mi.itemID, s.qty
    FROM (VALUES
        ('BEV-COKE-1L',  48),
        ('BEV-SPRT-1L',  35),
        ('CAN-SRD-155',  120),
        ('CAN-CRND-150', 12),
        ('DAI-MLK-1L',   0),
        ('PRO-BANA-1K',  22),
        ('BEV-WTR-1L',   50),
        ('PRO-ON-100',   34),
        ('DAI-CHS-1',    42),
        ('MEAT-CHKN-1',  74),
        ('MEAT-BF-500g', 0),
        ('MEAT-PRK-500g',46)
    ) AS s(code, qty)
    INNER JOIN dbo.mart_items mi ON mi.itemCode = s.code
)
MERGE dbo.stock AS target
USING src ON target.itemID = src.itemID
WHEN MATCHED THEN
    UPDATE SET currentQty = src.qty, lastUpdated = GETDATE()
WHEN NOT MATCHED THEN
    INSERT (itemID, currentQty, lastUpdated)
    VALUES (src.itemID, src.qty, GETDATE());
GO

-- =========================================
-- 5) Verification queries
-- =========================================
SELECT 'user_accounts' AS [Table], COUNT(*) AS [Rows] FROM dbo.user_accounts
UNION ALL SELECT 'supplier', COUNT(*) FROM dbo.supplier
UNION ALL SELECT 'mart_items', COUNT(*) FROM dbo.mart_items
UNION ALL SELECT 'stock', COUNT(*) FROM dbo.stock
UNION ALL SELECT 'inventory', COUNT(*) FROM dbo.inventory
UNION ALL SELECT 'transaction', COUNT(*) FROM dbo.[transaction];
GO
