-- =====================================================================
-- Day 9 - Task 2: Reproduce a classic two-resource deadlock
-- =====================================================================
-- Target: same standalone SQL Server 2022 container
-- ("day8-mssql-experiment") and the same Day8IndexDemo.dbo.Orders table
-- used by day - 8 and day - 9/task - 1. No new table - "Resource A" and
-- "Resource B" are just two existing rows, OrderId 11111 and OrderId
-- 22222, picked because they're untouched by any other task.
--
-- Both sessions UPDATE Amount to itself (Amount + 0.00) - this takes a
-- real row-level X lock without changing any data, so a clean deadlock
-- (with automatic rollback of the victim) leaves the table exactly as
-- it started. No manual data cleanup is needed after this file.
--
-- Run Section 0 once (any window). Then open TWO more query windows -
-- SESSION 1 and SESSION 2 - and run each in full, starting Session 2
-- within a couple of seconds of Session 1 (both have a 5s WAITFOR
-- before reaching for the other's row, so a small startup gap is fine).
-- One of the two will end with Msg 1205 (deadlock victim) - that's the
-- expected result, not a failure. Finish with Section 3 in either
-- window to pull the deadlock graph and remove the XE session.
-- =====================================================================


-- =====================================================================
-- SECTION 0 - Start capturing the deadlock graph (Extended Events)
-- =====================================================================
IF EXISTS (SELECT 1 FROM sys.server_event_sessions WHERE name = 'CaptureDeadlock_Day9')
BEGIN
    IF EXISTS (SELECT 1 FROM sys.dm_xe_sessions WHERE name = 'CaptureDeadlock_Day9')
        ALTER EVENT SESSION CaptureDeadlock_Day9 ON SERVER STATE = STOP;
    DROP EVENT SESSION CaptureDeadlock_Day9 ON SERVER;
END;
GO
CREATE EVENT SESSION CaptureDeadlock_Day9 ON SERVER
ADD EVENT sqlserver.xml_deadlock_report
ADD TARGET package0.event_file(SET filename = N'/var/opt/mssql/data/deadlock_day9.xel')
WITH (STARTUP_STATE = OFF);
GO
ALTER EVENT SESSION CaptureDeadlock_Day9 ON SERVER STATE = START;
GO


-- =====================================================================
-- SESSION 1 - Resource A (11111) then Resource B (22222)
-- =====================================================================
USE Day8IndexDemo;
GO
SET LOCK_TIMEOUT -1;
BEGIN TRANSACTION;
UPDATE dbo.Orders SET Amount = Amount + 0.00 WHERE OrderId = 11111;  -- locks Resource A
WAITFOR DELAY '00:00:05';                                            -- gives Session 2 time to lock Resource B
UPDATE dbo.Orders SET Amount = Amount + 0.00 WHERE OrderId = 22222;  -- now waits on Session 2's lock on B
COMMIT TRANSACTION;
SELECT 'session1 completed' AS Result;
GO
-- Expected on the victim: Msg 1205, "... was chosen as the deadlock
-- victim. Rerun the transaction." The other session's Result row
-- ("session1 completed" or "session2 completed") shows which one won.


-- =====================================================================
-- SESSION 2 - Resource B (22222) then Resource A (11111)
-- =====================================================================
USE Day8IndexDemo;
GO
SET LOCK_TIMEOUT -1;
BEGIN TRANSACTION;
UPDATE dbo.Orders SET Amount = Amount + 0.00 WHERE OrderId = 22222;  -- locks Resource B
WAITFOR DELAY '00:00:05';                                            -- gives Session 1 time to lock Resource A
UPDATE dbo.Orders SET Amount = Amount + 0.00 WHERE OrderId = 11111;  -- now waits on Session 1's lock on A
COMMIT TRANSACTION;
SELECT 'session2 completed' AS Result;
GO
-- Session 1 holds A, wants B. Session 2 holds B, wants A. Circular wait -
-- SQL Server's lock monitor detects it and kills one side automatically.


-- =====================================================================
-- SECTION 3 - Pull the deadlock graph, confirm state, clean up
-- =====================================================================
ALTER EVENT SESSION CaptureDeadlock_Day9 ON SERVER STATE = STOP;
GO
SELECT CAST(event_data AS XML) AS event_data
FROM sys.fn_xe_file_target_read_file('/var/opt/mssql/data/deadlock_day9*.xel', NULL, NULL, NULL);
GO
-- The single row returned is the full deadlock graph (victim-list,
-- both processes' inputbuf, resource-list) - see deadlock-graph.xml for
-- the graph captured this way and README.md for how to read it.

-- Confirm both original rows are unchanged and the table wasn't left
-- locked or short a row:
SELECT OrderId, CustomerId, Amount FROM dbo.Orders WHERE OrderId IN (11111, 22222);
SELECT COUNT(*) AS TotalRows FROM dbo.Orders;
GO

DROP EVENT SESSION CaptureDeadlock_Day9 ON SERVER;
GO
