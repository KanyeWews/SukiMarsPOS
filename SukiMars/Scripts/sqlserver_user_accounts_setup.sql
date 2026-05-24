-- SQL Server version of user_accounts for SukiMars login.
IF OBJECT_ID(N'dbo.user_accounts', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.user_accounts
    (
        userID INT IDENTITY(1,1) PRIMARY KEY,
        firstName VARCHAR(100) NULL,
        lastName VARCHAR(100) NULL,
        username VARCHAR(100) NOT NULL UNIQUE,
        [password] VARCHAR(255) NOT NULL,
        pincode VARCHAR(20) NULL,
        userRole VARCHAR(50) NOT NULL,
        [status] VARCHAR(50) NOT NULL
    );
END;
GO

-- Seed one account per role for testing login.
IF NOT EXISTS (SELECT 1 FROM dbo.user_accounts WHERE username = 'cashier1')
BEGIN
    INSERT INTO dbo.user_accounts (firstName, lastName, username, [password], pincode, userRole, [status])
    VALUES ('Cashier', 'One', 'cashier1', '1234', '1111', 'Cashier', 'Active');
END;
GO

IF NOT EXISTS (SELECT 1 FROM dbo.user_accounts WHERE username = 'manager1')
BEGIN
    INSERT INTO dbo.user_accounts (firstName, lastName, username, [password], pincode, userRole, [status])
    VALUES ('Manager', 'One', 'manager1', '1234', '2222', 'Manager', 'Active');
END;
GO

IF NOT EXISTS (SELECT 1 FROM dbo.user_accounts WHERE username = 'admin1')
BEGIN
    INSERT INTO dbo.user_accounts (firstName, lastName, username, [password], pincode, userRole, [status])
    VALUES ('Admin', 'One', 'admin1', '1234', '3333', 'Admin', 'Active');
END;
GO
