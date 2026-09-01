-- =====================================================================
-- Day 9 - Task 2: Fixed version - consistent lock ordering
-- =====================================================================
-- Same two rows as deadlock-repro.sql (OrderId 11111 = Resource A,
-- OrderId 22222 = Resource B), same container/table. The only change
-- from deadlock-repro.sql: Session 2 now takes Resource A before
-- Resource B, same as Session 1 - both sessions acquire the two rows
-- in the same order.
--
-- Open two query windows and run these in the same overlapping way as
-- deadlock-repro.sql (start Session 2 a couple of seconds after
-- Session 1). This time there is no circular wait: whichever session
-- gets to Resource A first, the other one simply blocks on that same
-- row until the first commits, then proceeds normally. Both complete -
-- no Msg 1205, no victim.
-- =====================================================================


-- =====================================================================
-- SESSION 1 - Resource A (11111) then Resource B (22222)
-- =====================================================================
USE Day8IndexDemo;
GO
SET LOCK_TIMEOUT -1;
BEGIN TRANSACTION;
UPDATE dbo.Orders SET Amount = Amount + 0.00 WHERE OrderId = 11111;  -- locks Resource A
WAITFOR DELAY '00:00:05';
UPDATE dbo.Orders SET Amount = Amount + 0.00 WHERE OrderId = 22222;  -- locks Resource B (uncontested)
COMMIT TRANSACTION;
SELECT 'session1 completed' AS Result;
GO


-- =====================================================================
-- SESSION 2 - Resource A (11111) then Resource B (22222) - same order
-- =====================================================================
USE Day8IndexDemo;
GO
SET LOCK_TIMEOUT -1;
BEGIN TRANSACTION;
UPDATE dbo.Orders SET Amount = Amount + 0.00 WHERE OrderId = 11111;  -- blocks here until Session 1 commits
WAITFOR DELAY '00:00:05';
UPDATE dbo.Orders SET Amount = Amount + 0.00 WHERE OrderId = 22222;
COMMIT TRANSACTION;
SELECT 'session2 completed' AS Result;
GO
-- Expected: both sessions print their "completed" row - no deadlock.
-- Session 2's first UPDATE simply waits (ordinary lock wait, not a
-- circular one) until Session 1's transaction ends.


-- =====================================================================
-- Confirm no deadlock, table unchanged
-- =====================================================================
USE Day8IndexDemo;
GO
SELECT OrderId, CustomerId, Amount FROM dbo.Orders WHERE OrderId IN (11111, 22222);
SELECT COUNT(*) AS TotalRows FROM dbo.Orders;
GO
