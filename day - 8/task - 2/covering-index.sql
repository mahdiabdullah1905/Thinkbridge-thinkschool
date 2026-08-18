-- =====================================================================
-- Day 8 - Task 2: Covering indexes and INCLUDEd columns
-- =====================================================================
-- Reuses the exact table/data/indexes built in
-- day - 8/task - 1/index-comparison.sql (same standalone SQL Server 2022
-- container "day8-mssql-experiment", database Day8IndexDemo, table
-- dbo.Orders). Nothing from Task 1 is modified here - Section 2 below
-- only ADDS one new index; it does not alter or drop any Task 1 object.
--
-- Prerequisite: day - 8/task - 1/index-comparison.sql Sections 1, 2, 4
-- and 5 have already been run, so before this script starts:
--   - dbo.Orders already has 100,000 deterministic rows
--   - CIX_Orders_OrderId   (clustered, on OrderId) already exists
--   - IX_Orders_CustomerId (non-clustered, on CustomerId,
--                           INCLUDE (OrderDate, Amount)) already exists
-- =====================================================================

USE Day8IndexDemo;
GO


-- =====================================================================
-- SECTION 1 - Baseline: a query that Key-Lookups against IX_Orders_CustomerId
-- =====================================================================
-- IX_Orders_CustomerId covers CustomerId (the key), OrderDate and Amount
-- (INCLUDEd), and OrderId (the clustering key, always carried by a
-- non-clustered index as the row locator) - but NOT Notes. Asking for
-- Notes forces a Key Lookup back into the clustered index for every
-- matching row.
SET STATISTICS IO ON;
SET STATISTICS XML ON;
GO

SELECT OrderId, CustomerId, OrderDate, Amount, Notes
FROM dbo.Orders
WHERE CustomerId = 777;
GO

SET STATISTICS XML OFF;
SET STATISTICS IO OFF;
GO

-- Actual plan captured against this data: Index Seek on
-- IX_Orders_CustomerId (ActualLogicalReads=3) -> Nested Loops ->
-- Clustered Index Seek on CIX_Orders_OrderId, with Lookup="1" set on
-- that RelOp in the plan XML (ActualLogicalReads=270) - Lookup="1" is
-- exactly the attribute SSMS/Azure Data Studio renders as a "Key
-- Lookup" operator. Table 'Orders' total: 318 logical reads.


-- =====================================================================
-- SECTION 2 - Add a covering index (widen INCLUDE to add Notes), re-run
-- =====================================================================
CREATE NONCLUSTERED INDEX IX_Orders_CustomerId_Covering
    ON dbo.Orders (CustomerId)
    INCLUDE (OrderDate, Amount, Notes);
GO

SET STATISTICS IO ON;
SET STATISTICS XML ON;
GO

SELECT OrderId, CustomerId, OrderDate, Amount, Notes
FROM dbo.Orders
WHERE CustomerId = 777;
GO

SET STATISTICS XML OFF;
SET STATISTICS IO OFF;
GO

-- Actual plan captured against this data: a single RelOp,
-- PhysicalOp="Index Seek" on IX_Orders_CustomerId_Covering,
-- ActualLogicalReads=7. No Nested Loops, no Clustered Index Seek, no
-- Lookup="1" anywhere in the XML - the Key Lookup is gone. Table
-- 'Orders' total: 7 logical reads (down from 318).
