-- =====================================================================
-- Day 9 - Task 1: Non-repeatable read
-- =====================================================================
-- Same container/table as dirty-read.sql: day8-mssql-experiment,
-- Day8IndexDemo.dbo.Orders, OrderId 54321 (baseline Amount = 4321.50).
--
-- Two query windows again, labeled SESSION 1 / SESSION 2. Run steps in
-- the numbered order, switching windows where the comments say to.
-- =====================================================================


-- =====================================================================
-- PART A - ANOMALY: non-repeatable read under READ COMMITTED
-- =====================================================================
-- READ COMMITTED only guarantees a row isn't read while uncommitted -
-- it does NOT hold the read lock for the rest of the transaction, so a
-- second read of the same row can see a value someone else committed in
-- between.

-- --- SESSION 1 - Step 1 ---
-- Run this, then leave the transaction open (don't run Step 3 yet).
USE Day8IndexDemo;
GO
SET TRANSACTION ISOLATION LEVEL READ COMMITTED;
BEGIN TRANSACTION;
SELECT Amount AS FirstRead FROM dbo.Orders WHERE OrderId = 54321;
GO
-- Expected: FirstRead = 4321.50

-- --- SESSION 2 - Step 2 ---
-- Run this while Session 1's transaction is still open. This is a
-- single autocommit statement, so it commits immediately - READ
-- COMMITTED already released Session 1's read lock after Step 1's
-- SELECT finished, so this is not blocked.
USE Day8IndexDemo;
GO
UPDATE dbo.Orders SET Amount = 5000.00 WHERE OrderId = 54321;
GO

-- --- SESSION 1 - Step 3 ---
-- Same open transaction as Step 1, same row, no write from this session
-- in between - yet the value has changed underneath it.
SELECT Amount AS SecondRead FROM dbo.Orders WHERE OrderId = 54321;
COMMIT TRANSACTION;
GO
-- Expected: SecondRead = 5000.00 (!= FirstRead) - non-repeatable read.

-- --- Cleanup (either session) ---
-- Restore the baseline so the prevention demo below (and any re-run of
-- this file) starts from the same known value.
UPDATE dbo.Orders SET Amount = 4321.50 WHERE OrderId = 54321;
GO


-- =====================================================================
-- PART B - PREVENTION: REPEATABLE READ
-- =====================================================================
-- Lowest isolation level that prevents this. REPEATABLE READ holds a
-- shared lock on every row it reads until the transaction ends, so a
-- concurrent UPDATE of that row has to wait.

-- --- SESSION 1 - Step 1 ---
USE Day8IndexDemo;
GO
SET TRANSACTION ISOLATION LEVEL REPEATABLE READ;
BEGIN TRANSACTION;
SELECT Amount AS FirstRead FROM dbo.Orders WHERE OrderId = 54321;
GO
-- Expected: FirstRead = 4321.50

-- --- SESSION 2 - Step 2 ---
-- Run this while Session 1's transaction is still open. This time it
-- BLOCKS - Session 1's shared lock from Step 1 is still held, so the
-- UPDATE has to wait for Session 1 to finish. Leave this window waiting
-- and switch back to Session 1.
USE Day8IndexDemo;
GO
UPDATE dbo.Orders SET Amount = 5000.00 WHERE OrderId = 54321;
GO

-- --- SESSION 1 - Step 3 ---
-- Session 2's update above is still blocked, so this rereads the same
-- unchanged row.
SELECT Amount AS SecondRead FROM dbo.Orders WHERE OrderId = 54321;
GO
-- Expected: SecondRead = 4321.50, same as FirstRead - value held stable
-- for the life of the transaction.

-- --- SESSION 1 - Step 4 ---
COMMIT TRANSACTION;
GO
-- Committing releases the shared lock - switch to Session 2 and its
-- blocked UPDATE from Step 2 will complete right away.

-- --- Cleanup (either session, after Session 2's update lands) ---
UPDATE dbo.Orders SET Amount = 4321.50 WHERE OrderId = 54321;
GO
