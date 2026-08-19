-- =====================================================================
-- Day 9 - Task 1: Phantom read
-- =====================================================================
-- Same container/table again: day8-mssql-experiment,
-- Day8IndexDemo.dbo.Orders. Uses CustomerId = 9999, which does not
-- exist in the 100,000-row baseline (CustomerId only ranges 1..1000 -
-- see day - 8/task - 1's Section 2), so COUNT(*) starts at a clean 0.
--
-- Two query windows, SESSION 1 / SESSION 2. Run steps in order,
-- switching windows where the comments say to.
-- =====================================================================


-- =====================================================================
-- PART A - ANOMALY: phantom read under REPEATABLE READ
-- =====================================================================
-- REPEATABLE READ locks the rows it has already read so they can't
-- change - but it does not lock the "gap" a predicate covers, so a
-- second execution of the same query can pick up rows that didn't exist
-- on the first pass. That's a phantom, and it's a different anomaly
-- from non-repeatable-read.sql: no existing row changed, a new one
-- appeared.

-- --- SESSION 1 - Step 1 ---
-- Run this, then leave the transaction open (don't run Step 3 yet).
USE Day8IndexDemo;
GO
SET TRANSACTION ISOLATION LEVEL REPEATABLE READ;
BEGIN TRANSACTION;
SELECT COUNT(*) AS FirstCount FROM dbo.Orders WHERE CustomerId = 9999;
GO
-- Expected: FirstCount = 0

-- --- SESSION 2 - Step 2 ---
-- Run this while Session 1's transaction is still open. Nothing this
-- inserts is one of the rows Session 1 already read, so there's no row
-- lock in its way - this commits immediately, unblocked.
USE Day8IndexDemo;
GO
INSERT INTO dbo.Orders (OrderId, CustomerId, OrderDate, Status, Amount, Notes)
VALUES (200001, 9999, '2026-08-19', 'Pending', 42.00, REPLICATE('X', 200));
GO

-- --- SESSION 1 - Step 3 ---
-- Same open transaction, same predicate, no re-read of any row Session 1
-- already touched - just a brand new row matching the WHERE clause.
SELECT COUNT(*) AS SecondCount FROM dbo.Orders WHERE CustomerId = 9999;
COMMIT TRANSACTION;
GO
-- Expected: SecondCount = 1 (!= FirstCount) - a phantom row.

-- --- Cleanup (either session) ---
DELETE FROM dbo.Orders WHERE OrderId = 200001;
GO


-- =====================================================================
-- PART B - PREVENTION: SERIALIZABLE
-- =====================================================================
-- Lowest isolation level that prevents this. SERIALIZABLE takes a
-- range/key-range lock covering the predicate itself (CustomerId = 9999),
-- not just the rows it found, so a concurrent INSERT of a matching row
-- has to wait for that range lock to be released.

-- --- SESSION 1 - Step 1 ---
USE Day8IndexDemo;
GO
SET TRANSACTION ISOLATION LEVEL SERIALIZABLE;
BEGIN TRANSACTION;
SELECT COUNT(*) AS FirstCount FROM dbo.Orders WHERE CustomerId = 9999;
GO
-- Expected: FirstCount = 0

-- --- SESSION 2 - Step 2 ---
-- Run this while Session 1's transaction is still open. This time it
-- BLOCKS on Session 1's range lock over CustomerId = 9999. Leave this
-- window waiting and switch back to Session 1.
USE Day8IndexDemo;
GO
INSERT INTO dbo.Orders (OrderId, CustomerId, OrderDate, Status, Amount, Notes)
VALUES (200002, 9999, '2026-08-19', 'Pending', 42.00, REPLICATE('X', 200));
GO

-- --- SESSION 1 - Step 3 ---
-- Session 2's insert above is still blocked, so the count can't have
-- changed.
SELECT COUNT(*) AS SecondCount FROM dbo.Orders WHERE CustomerId = 9999;
GO
-- Expected: SecondCount = 0, same as FirstCount - no phantom.

-- --- SESSION 1 - Step 4 ---
COMMIT TRANSACTION;
GO
-- Releases the range lock - switch to Session 2 and its blocked INSERT
-- from Step 2 will complete right away.

-- --- Cleanup (either session, after Session 2's insert lands) ---
DELETE FROM dbo.Orders WHERE OrderId = 200002;
GO
