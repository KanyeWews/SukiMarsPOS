USE SukiMarsDB;
GO

/* =========================
   1) USERS (role-based)
   ========================= */
IF NOT EXISTS (SELECT 1 FROM dbo.user_accounts WHERE username = 'cashier1')
BEGIN
    INSERT INTO dbo.user_accounts (firstName, lastName, username, [password], pincode, userRole, [status])
    VALUES ('Cashier', 'One', 'cashier1', '1234', '1111', 'Cashier', 'Active');
END;

IF NOT EXISTS (SELECT 1 FROM dbo.user_accounts WHERE username = 'manager1')
BEGIN
    INSERT INTO dbo.user_accounts (firstName, lastName, username, [password], pincode, userRole, [status])
    VALUES ('Manager', 'One', 'manager1', '1234', '2222', 'Manager', 'Active');
END;

IF NOT EXISTS (SELECT 1 FROM dbo.user_accounts WHERE username = 'admin1')
BEGIN
    INSERT INTO dbo.user_accounts (firstName, lastName, username, [password], pincode, userRole, [status])
    VALUES ('Admin', 'One', 'admin1', '1234', '3333', 'Admin', 'Active');
END;
GO


/* =========================
   2) SUPPLIERS
   ========================= */
IF NOT EXISTS (SELECT 1 FROM dbo.supplier WHERE supplierName = 'Fresh Source Trading')
BEGIN
    INSERT INTO dbo.supplier (supplierName, contact)
    VALUES ('Fresh Source Trading', '0917-111-1001');
END;

IF NOT EXISTS (SELECT 1 FROM dbo.supplier WHERE supplierName = 'Metro Canned Depot')
BEGIN
    INSERT INTO dbo.supplier (supplierName, contact)
    VALUES ('Metro Canned Depot', '0917-111-1002');
END;

IF NOT EXISTS (SELECT 1 FROM dbo.supplier WHERE supplierName = 'Luzon Dairy Distribution')
BEGIN
    INSERT INTO dbo.supplier (supplierName, contact)
    VALUES ('Luzon Dairy Distribution', '0917-111-1003');
END;
GO

/* =========================
   3) MART ITEMS
   ========================= */
IF NOT EXISTS (SELECT 1 FROM dbo.mart_items WHERE itemCode = 'BEV-COKE-1L')
BEGIN
    INSERT INTO dbo.mart_items (itemName, itemCode, itemCategory, itemDescription, price)
    VALUES ('Coke 1L', 'BEV-COKE-1L', 'Beverages', 'Carbonated drink 1 liter', 55.00);
END;

IF NOT EXISTS (SELECT 1 FROM dbo.mart_items WHERE itemCode = 'BEV-SPRITE-1L')
BEGIN
    INSERT INTO dbo.mart_items (itemName, itemCode, itemCategory, itemDescription, price)
    VALUES ('Sprite 1L', 'BEV-SPRITE-1L', 'Beverages', 'Lemon-lime soda 1 liter', 55.00);
END;

IF NOT EXISTS (SELECT 1 FROM dbo.mart_items WHERE itemCode = 'CAN-SARD-155')
BEGIN
    INSERT INTO dbo.mart_items (itemName, itemCode, itemCategory, itemDescription, price)
    VALUES ('Sardines 155g', 'CAN-SARD-155', 'Canned Goods', 'Tomato sardines 155g', 28.00);
END;

IF NOT EXISTS (SELECT 1 FROM dbo.mart_items WHERE itemCode = 'CAN-CORNED-150')
BEGIN
    INSERT INTO dbo.mart_items (itemName, itemCode, itemCategory, itemDescription, price)
    VALUES ('Corned Beef 150g', 'CAN-CORNED-150', 'Canned Goods', 'Premium corned beef 150g', 65.00);
END;

IF NOT EXISTS (SELECT 1 FROM dbo.mart_items WHERE itemCode = 'DAI-MILK-1L')
BEGIN
    INSERT INTO dbo.mart_items (itemName, itemCode, itemCategory, itemDescription, price)
    VALUES ('Fresh Milk 1L', 'DAI-MILK-1L', 'Dairy', 'Fresh milk 1 liter', 78.00);
END;

IF NOT EXISTS (SELECT 1 FROM dbo.mart_items WHERE itemCode = 'PRO-BANA-1K')
BEGIN
    INSERT INTO dbo.mart_items (itemName, itemCode, itemCategory, itemDescription, price)
    VALUES ('Banana 1kg', 'PRO-BANA-1K', 'Produce', 'Lakatan banana 1 kilogram', 90.00);
END;
GO

/* =========================
   4) STOCK (upsert style)
   ========================= */
;WITH src AS
(
    SELECT itemID, qty
    FROM
    (
        SELECT (SELECT itemID FROM dbo.mart_items WHERE itemCode = 'BEV-COKE-1L') AS itemID, 48 AS qty
        UNION ALL SELECT (SELECT itemID FROM dbo.mart_items WHERE itemCode = 'BEV-SPRITE-1L'), 35
        UNION ALL SELECT (SELECT itemID FROM dbo.mart_items WHERE itemCode = 'CAN-SARD-155'), 120
        UNION ALL SELECT (SELECT itemID FROM dbo.mart_items WHERE itemCode = 'CAN-CORNED-150'), 12
        UNION ALL SELECT (SELECT itemID FROM dbo.mart_items WHERE itemCode = 'DAI-MILK-1L'), 0
        UNION ALL SELECT (SELECT itemID FROM dbo.mart_items WHERE itemCode = 'PRO-BANA-1K'), 22
    ) q
    WHERE itemID IS NOT NULL
)
MERGE dbo.stock AS target
USING src
ON target.itemID = src.itemID
WHEN MATCHED THEN
    UPDATE SET currentQty = src.qty, lastUpdated = GETDATE()
WHEN NOT MATCHED THEN
    INSERT (itemID, currentQty, lastUpdated)
    VALUES (src.itemID, src.qty, GETDATE());
GO

/* =========================
   5) INVENTORY + DETAILS
   ========================= */
DECLARE @managerId INT = (SELECT TOP 1 userID FROM dbo.user_accounts WHERE username = 'manager1');
DECLARE @supplier1 INT = (SELECT TOP 1 supplierID FROM dbo.supplier WHERE supplierName = 'Fresh Source Trading');

IF @managerId IS NOT NULL AND @supplier1 IS NOT NULL
AND NOT EXISTS (SELECT 1 FROM dbo.inventory WHERE acquisitionDate = '2026-04-25' AND userID = @managerId)
BEGIN
    INSERT INTO dbo.inventory (acquisitionDate, deliveryDate, supplierID, acquisitionCost, userID, deliveryStatus)
    VALUES ('2026-04-25', '2026-04-26', @supplier1, 12500.00, @managerId, 'Delivered');

    DECLARE @inventoryID INT = SCOPE_IDENTITY();

    INSERT INTO dbo.inventory_details (inventoryID, itemID, quantity)
    SELECT @inventoryID, itemID, quantity
    FROM
    (
        SELECT (SELECT itemID FROM dbo.mart_items WHERE itemCode = 'BEV-COKE-1L') AS itemID, 60 AS quantity
        UNION ALL SELECT (SELECT itemID FROM dbo.mart_items WHERE itemCode = 'CAN-SARD-155'), 150
        UNION ALL SELECT (SELECT itemID FROM dbo.mart_items WHERE itemCode = 'DAI-MILK-1L'), 40
    ) x
    WHERE itemID IS NOT NULL;
END;
GO

/* =========================
   6) TRANSACTION + DETAILS
   ========================= */
DECLARE @cashierId INT = (SELECT TOP 1 userID FROM dbo.user_accounts WHERE username = 'cashier1');

IF @cashierId IS NOT NULL
AND NOT EXISTS (SELECT 1 FROM dbo.[transaction] WHERE invoiceNumber = 'INV-2026-0001')
BEGIN
    INSERT INTO dbo.[transaction]
    (
        transDateTime, discountAmount, totalItem, totalAmount,
        VATableSales, VAT, invoiceNumber, paymentMethod, userID
    )
    VALUES
    (
        DATEADD(MINUTE, -20, GETDATE()), 0.00, 4, 193.00,
        172.32, 20.68, 'INV-2026-0001', 'Cash', @cashierId
    );

    DECLARE @transID1 INT = SCOPE_IDENTITY();

    INSERT INTO dbo.transaction_details (transactionID, itemID, qty, unitPrice, total)
    SELECT @transID1, itemID, qty, unitPrice, total
    FROM
    (
        SELECT (SELECT itemID FROM dbo.mart_items WHERE itemCode = 'BEV-COKE-1L') AS itemID, 1 AS qty, 55.00 AS unitPrice, 55.00 AS total
        UNION ALL
        SELECT (SELECT itemID FROM dbo.mart_items WHERE itemCode = 'CAN-SARD-155'), 2, 28.00, 56.00
        UNION ALL
        SELECT (SELECT itemID FROM dbo.mart_items WHERE itemCode = 'DAI-MILK-1L'), 1, 78.00, 78.00
    ) d
    WHERE itemID IS NOT NULL;
END;

IF @cashierId IS NOT NULL
AND NOT EXISTS (SELECT 1 FROM dbo.[transaction] WHERE invoiceNumber = 'INV-2026-0002')
BEGIN
    INSERT INTO dbo.[transaction]
    (
        transDateTime, discountAmount, totalItem, totalAmount,
        VATableSales, VAT, invoiceNumber, paymentMethod, userID
    )
    VALUES
    (
        DATEADD(MINUTE, -5, GETDATE()), 10.00, 3, 188.00,
        167.86, 20.14, 'INV-2026-0002', 'GCash', @cashierId
    );

    DECLARE @transID2 INT = SCOPE_IDENTITY();

    INSERT INTO dbo.transaction_details (transactionID, itemID, qty, unitPrice, total)
    SELECT @transID2, itemID, qty, unitPrice, total
    FROM
    (
        SELECT (SELECT itemID FROM dbo.mart_items WHERE itemCode = 'CAN-CORNED-150') AS itemID, 1 AS qty, 65.00 AS unitPrice, 65.00 AS total
        UNION ALL
        SELECT (SELECT itemID FROM dbo.mart_items WHERE itemCode = 'BEV-SPRITE-1L'), 1, 55.00, 55.00
        UNION ALL
        SELECT (SELECT itemID FROM dbo.mart_items WHERE itemCode = 'PRO-BANA-1K'), 1, 78.00, 78.00
    ) d
    WHERE itemID IS NOT NULL;
END;
GO

/* =========================
   7) QUICK CHECKS
   ========================= */
SELECT userID, username, userRole, [status] FROM dbo.user_accounts ORDER BY userID;
SELECT supplierID, supplierName FROM dbo.supplier ORDER BY supplierID;
SELECT itemID, itemName, itemCode, itemCategory, price FROM dbo.mart_items ORDER BY itemID;
SELECT stockID, itemID, currentQty, lastUpdated FROM dbo.stock ORDER BY stockID;
SELECT transactionID, invoiceNumber, totalAmount, userID, transDateTime FROM dbo.[transaction] ORDER BY transactionID DESC;
GO
