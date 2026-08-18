-- =====================================================================
-- Day 8 - Task 1: Clustered vs non-clustered indexes
-- =====================================================================
-- Target: standalone SQL Server 2022 container ("day8-mssql-experiment"),
-- started manually from the mcr.microsoft.com/mssql/server:2022-latest
-- image already used by day - 3/task - 7's Testcontainers integration
-- tests. This is a separate, standalone container for this experiment
-- only - it is not the Testcontainers-managed instance.
--
-- Run each numbered section IN ORDER, one at a time, with "Include Actual
-- Execution Plan" turned on if you're doing this in SSMS/Azure Data
-- Studio. The queries in Sections 3, 5 and 6 are deliberately re-run
-- after each index is added so before/after logical reads and plans can
-- be compared for the same query.
-- =====================================================================


-- =====================================================================
-- SECTION 1 - Database and heap table (no indexes yet)
-- =====================================================================
CREATE DATABASE Day8IndexDemo;
GO

USE Day8IndexDemo;
GO

CREATE TABLE dbo.Orders (
    OrderId    INT          NOT NULL,
    CustomerId INT          NOT NULL,
    OrderDate  DATE         NOT NULL,
    Status     VARCHAR(20)  NOT NULL,
    Amount     DECIMAL(10,2) NOT NULL,
    Notes      CHAR(200)    NOT NULL   -- pads row size so a table scan costs a realistic number of pages
);
GO


-- =====================================================================
-- SECTION 2 - Deterministic ~100,000-row load
-- =====================================================================
-- Every column is derived from the row number (n) alone - no RAND(),
-- NEWID() or CHECKSUM(). Re-running this against a fresh database
-- produces byte-identical data every time.
--   CustomerId : 1..1000, each customer gets exactly 100 orders
--   OrderDate  : cycles through a 730-day window from 2024-01-01
--   Status     : cycles through 5 fixed values
--   Amount     : deterministic 0.50..5000.50 sawtooth
;WITH L0 AS (SELECT 1 AS c UNION ALL SELECT 1),
L1 AS (SELECT 1 AS c FROM L0 A CROSS JOIN L0 B),
L2 AS (SELECT 1 AS c FROM L1 A CROSS JOIN L1 B),
L3 AS (SELECT 1 AS c FROM L2 A CROSS JOIN L2 B),
L4 AS (SELECT 1 AS c FROM L3 A CROSS JOIN L3 B),
L5 AS (SELECT 1 AS c FROM L4 A CROSS JOIN L4 B),
Nums AS (SELECT ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) AS n FROM L5)
INSERT INTO dbo.Orders (OrderId, CustomerId, OrderDate, Status, Amount, Notes)
SELECT TOP (100000)
    n                                   AS OrderId,
    ((n - 1) % 1000) + 1                AS CustomerId,
    DATEADD(DAY, (n - 1) % 730, '2024-01-01') AS OrderDate,
    CASE (n % 5)
        WHEN 0 THEN 'Pending'
        WHEN 1 THEN 'Shipped'
        WHEN 2 THEN 'Delivered'
        WHEN 3 THEN 'Cancelled'
        ELSE 'Returned'
    END                                 AS Status,
    CAST(((n % 5000) + 0.50) AS DECIMAL(10,2)) AS Amount,
    REPLICATE('X', 200)                 AS Notes
FROM Nums;
GO

SELECT COUNT(*) AS [RowCount] FROM dbo.Orders;
GO


-- =====================================================================
-- SECTION 3 - Baseline queries against the heap (NO indexes)
-- =====================================================================
-- Run with SET STATISTICS IO ON (and, in SSMS/ADS, Actual Execution Plan
-- turned on) BEFORE any index exists. Every one of these has to scan the
-- whole heap, because there is no structure the optimizer can seek into.

SET STATISTICS IO ON;
GO

-- Q1: point lookup on OrderId (this becomes the clustered index target)
SELECT OrderId, CustomerId, OrderDate, Status, Amount
FROM dbo.Orders
WHERE OrderId = 54321;
GO

-- Q2: equality lookup on CustomerId (this becomes non-clustered index #1's target)
SELECT OrderId, OrderDate, Amount
FROM dbo.Orders
WHERE CustomerId = 777;
GO

-- Q3: date-range lookup on OrderDate (this becomes non-clustered index #2's target)
SELECT OrderId, CustomerId, Status
FROM dbo.Orders
WHERE OrderDate BETWEEN '2024-06-01' AND '2024-06-07';
GO

SET STATISTICS IO OFF;
GO


-- =====================================================================
-- SECTION 4 - Add the clustered index, re-run Q1
-- =====================================================================
CREATE CLUSTERED INDEX CIX_Orders_OrderId ON dbo.Orders (OrderId);
GO

SET STATISTICS IO ON;
GO

SELECT OrderId, CustomerId, OrderDate, Status, Amount
FROM dbo.Orders
WHERE OrderId = 54321;
GO

SET STATISTICS IO OFF;
GO


-- =====================================================================
-- SECTION 5 - Add non-clustered index #1 (CustomerId), re-run Q2
-- =====================================================================
-- INCLUDE covers OrderDate/Amount so the seek doesn't need a separate
-- key lookup back into the clustered index.
CREATE NONCLUSTERED INDEX IX_Orders_CustomerId
    ON dbo.Orders (CustomerId)
    INCLUDE (OrderDate, Amount);
GO

SET STATISTICS IO ON;
GO

SELECT OrderId, OrderDate, Amount
FROM dbo.Orders
WHERE CustomerId = 777;
GO

SET STATISTICS IO OFF;
GO


-- =====================================================================
-- SECTION 6 - Add non-clustered index #2 (OrderDate), re-run Q3
-- =====================================================================
CREATE NONCLUSTERED INDEX IX_Orders_OrderDate
    ON dbo.Orders (OrderDate)
    INCLUDE (CustomerId, Status);
GO

SET STATISTICS IO ON;
GO

SELECT OrderId, CustomerId, Status
FROM dbo.Orders
WHERE OrderDate BETWEEN '2024-06-01' AND '2024-06-07';
GO

SET STATISTICS IO OFF;
GO


-- =====================================================================
-- SECTION 7 - Capturing the actual execution plan as XML (for sqlcmd)
-- =====================================================================
-- sqlcmd has no graphical plan viewer. SET STATISTICS XML ON returns the
-- real, post-execution "actual" plan (actual row counts included, not
-- estimated) as an XML result set. Save that XML to a .sqlplan file and
-- open it in SSMS or Azure Data Studio to see the graphical version and
-- confirm visually which physical operator/index each query used.
SET STATISTICS XML ON;
GO
SELECT OrderId, CustomerId, OrderDate, Status, Amount
FROM dbo.Orders WHERE OrderId = 54321;
GO
SELECT OrderId, OrderDate, Amount
FROM dbo.Orders WHERE CustomerId = 777;
GO
SELECT OrderId, CustomerId, Status
FROM dbo.Orders WHERE OrderDate BETWEEN '2024-06-01' AND '2024-06-07';
GO
SET STATISTICS XML OFF;
GO


-- =====================================================================
-- SECTION 8 - Write-side cost: same INSERT, heap vs. fully indexed
-- =====================================================================
-- Measures the actual logical-read cost of inserting the same 500 new
-- rows (OrderId 100001-100500) once with no indexes and once with the
-- clustered index + both non-clustered indexes in place, each time
-- inside a transaction that is rolled back so it doesn't change the
-- 100,000-row baseline used above.

-- 8a. Run this BEFORE Section 4 (heap, no indexes) for the "before" number:
BEGIN TRANSACTION;
SET STATISTICS IO ON;
INSERT INTO dbo.Orders (OrderId, CustomerId, OrderDate, Status, Amount, Notes)
SELECT TOP (500)
    100000 + ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) AS n,
    1, '2024-01-01', 'Pending', 1.00, REPLICATE('X', 200)
FROM sys.all_objects;
SET STATISTICS IO OFF;
ROLLBACK TRANSACTION;
GO

-- 8b. Run this again AFTER Section 6 (clustered + 2 non-clustered indexes exist)
-- for the "after" number - same statement, same row count, same rollback.
BEGIN TRANSACTION;
SET STATISTICS IO ON;
INSERT INTO dbo.Orders (OrderId, CustomerId, OrderDate, Status, Amount, Notes)
SELECT TOP (500)
    100000 + ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) AS n,
    1, '2024-01-01', 'Pending', 1.00, REPLICATE('X', 200)
FROM sys.all_objects;
SET STATISTICS IO OFF;
ROLLBACK TRANSACTION;
GO
