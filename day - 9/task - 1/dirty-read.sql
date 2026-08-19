-- =====================================================================
-- Day 9 - Task 1: Dirty read
-- =====================================================================
-- Target: same standalone SQL Server 2022 container
-- ("day8-mssql-experiment") and the same Day8IndexDemo.dbo.Orders table
-- used by day - 8/task - 1 and day - 8/task - 2. Nothing new was created -
-- this reuses OrderId 54321 (Amount = 4321.50 in the 100,000-row
-- deterministic load from day - 8/task - 1's Section 2).
--
-- Open TWO query windows connected to Day8IndexDemo - label them
-- SESSION 1 and SESSION 2. Run the numbered steps IN ORDER, switching
-- windows exactly where the comments say to. Do not run a whole window
-- top-to-bottom; each step is its own batch (separated by GO).
-- =====================================================================


-- =====================================================================
-- PART A - ANOMALY: dirty read under READ UNCOMMITTED
-- =====================================================================

-- --- SESSION 2 - Step 1 ---
-- Opens an update and deliberately does NOT commit. Run this and leave
-- the window sitting on the open transaction - do not run anything else
-- in this window until Step 3.
USE Day8IndexDemo;
GO
BEGIN TRANSACTION;
UPDATE dbo.Orders SET Amount = 99999.99 WHERE OrderId = 54321;
-- <- transaction left open here, uncommitted
GO

-- --- SESSION 1 - Step 2 ---
-- Run this now, while Session 2's transaction above is still open.
-- READ UNCOMMITTED (NOLOCK) ignores Session 2's exclusive lock and reads
-- the in-flight, uncommitted value straight out of the row.
USE Day8IndexDemo;
GO
SET TRANSACTION ISOLATION LEVEL READ UNCOMMITTED;
SELECT Amount AS DirtyRead FROM dbo.Orders WHERE OrderId = 54321;
GO
-- Expected: DirtyRead = 99999.99 - data that was never actually committed.

-- --- SESSION 2 - Step 3 ---
-- Back in Session 2: roll back. The 99999.99 Session 1 just "saw" never
-- existed as far as the database is concerned - that's what makes it a
-- dirty read rather than just an early read of real data.
ROLLBACK TRANSACTION;
GO

-- --- SESSION 1 - Step 4 (optional sanity check) ---
SELECT Amount AS AfterRollback FROM dbo.Orders WHERE OrderId = 54321;
GO
-- Expected: AfterRollback = 4321.50 (original value - confirms Step 2's
-- 99999.99 really was dirty, uncommitted data).


-- =====================================================================
-- PART B - PREVENTION: READ COMMITTED
-- =====================================================================
-- Lowest isolation level that prevents a dirty read. (This container's
-- Day8IndexDemo database has READ_COMMITTED_SNAPSHOT OFF, so READ
-- COMMITTED here is lock-based: Session 1's SELECT below will block
-- until Session 2 finishes, rather than returning instantly. See the
-- README for the RCSI note.)

-- --- SESSION 2 - Step 1 ---
USE Day8IndexDemo;
GO
BEGIN TRANSACTION;
UPDATE dbo.Orders SET Amount = 99999.99 WHERE OrderId = 54321;
-- <- transaction left open here, uncommitted
GO

-- --- SESSION 1 - Step 2 ---
-- Run this while Session 2's transaction is still open. Under READ
-- COMMITTED this blocks (spinning/"executing") instead of returning
-- 99999.99 - it is waiting for Session 2's exclusive lock to clear.
USE Day8IndexDemo;
GO
SET TRANSACTION ISOLATION LEVEL READ COMMITTED;
SELECT Amount AS ReadCommittedResult FROM dbo.Orders WHERE OrderId = 54321;
GO
-- Leave this batch running/blocked and switch to Session 2.

-- --- SESSION 2 - Step 3 ---
-- Roll back. As soon as this runs, Session 1's blocked SELECT above
-- unblocks and returns.
ROLLBACK TRANSACTION;
GO

-- Back in Session 1, Step 2's SELECT now completes with:
-- Expected: ReadCommittedResult = 4321.50 - the committed value, never
-- the in-flight 99999.99. No cleanup needed - both transactions above
-- roll back, so dbo.Orders is untouched.
