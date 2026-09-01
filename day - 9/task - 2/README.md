# Day 9 — Task 2: Reproduce and resolve a deadlock

## Setup

Same container and table as `day - 9/task - 1`: `day8-mssql-experiment`,
`Day8IndexDemo.dbo.Orders`. "Resource A" and "Resource B" are just two
existing rows, `OrderId = 11111` and `OrderId = 22222`, untouched by any
other task. Both scripts update `Amount` to itself
(`Amount = Amount + 0.00`) — a real row-level X lock with no actual data
change, so a deadlock's automatic rollback of the victim leaves the
table exactly as it started.

## Files

| File | What it does |
|---|---|
| `deadlock-repro.sql` | Sets up an Extended Events capture, then the two-session sequence that deadlocks |
| `deadlock-fix.sql` | Same two sessions, same two rows, fixed by acquiring them in the same order |
| `deadlock-graph.xml` | The actual deadlock graph captured from the repro run |

Open two query windows against `Day8IndexDemo` for the `SESSION 1` /
`SESSION 2` blocks in each file, run Session 1 first and Session 2 a
couple of seconds later (each has a 5s `WAITFOR` before reaching for the
other row, so a short gap is fine).

## The deadlock

`deadlock-repro.sql`:

- Session 1: locks `OrderId 11111`, waits, then tries `OrderId 22222`.
- Session 2: locks `OrderId 22222`, waits, then tries `OrderId 11111`.

Session 1 ends up holding A and waiting on B; Session 2 holds B and
waits on A — a genuine circular wait, not simulated. Run against the
live container:

```
Msg 1205, Level 13, State 51, Server 1c1a9ee3cb82, Line 5
Transaction (Process ID 52) was deadlocked on lock resources with
another process and has been chosen as the deadlock victim. Rerun
the transaction.
```

Session 2 (SPID 55) printed `session2 completed` and committed
normally; Session 1 (SPID 52) was the victim, its transaction rolled
back automatically.

## Deadlock graph

Captured with an Extended Events session on `sqlserver.xml_deadlock_report`
(commands are Section 0 / Section 3 of `deadlock-repro.sql`) and saved
to `deadlock-graph.xml` — the raw internal `<stackFrames>` blocks were
stripped for readability, everything else is exactly what
`sys.fn_xe_file_target_read_file` returned. The `resource-list` in that
file shows the two `keylock`s in `CIX_Orders_OrderId` directly:

- Lock on `11111`: owned X by SPID 55, waited on (U) by SPID 52
- Lock on `22222`: owned X by SPID 52, waited on (U) by SPID 55

— i.e. each session owns the lock the other is waiting on, and
`victim-list` names SPID 52's process as the one SQL Server killed.
Both sessions' `inputbuf` in the graph shows the real SQL text that was
running.

## The fix

`deadlock-fix.sql` changes only Session 2: it now locks `11111` before
`22222`, the same order as Session 1, instead of the reverse. Run
against the live container:

- Session 1: `session1 completed`
- Session 2: blocked on `11111` for ~5s (an ordinary lock wait, not a
  cycle) until Session 1 committed, then `session2 completed`

No `Msg 1205`, both transactions completed, and `Orders` was confirmed
unchanged (`Amount` still `1111.50` / `2222.50`, `100000` rows total)
after each run.

**Why it works:** a deadlock needs a cycle in the wait-for graph, and a
cycle can only form if two transactions lock the same pair of resources
in opposite orders — forcing every transaction to acquire them in the
same order makes one of them wait first, so the second lock request
never has to wait on someone who is in turn waiting on it.

## Validation performed

Both `deadlock-repro.sql` and `deadlock-fix.sql` were run against the
live `day8-mssql-experiment` container using two concurrent `sqlcmd`
sessions per file (not merely reasoned about). The repro produced the
real Msg 1205 above and the real deadlock graph in `deadlock-graph.xml`.
The fix was then run the same way and completed without error. After
each run: `dbo.Orders` was queried and confirmed at `100000` rows with
`Amount` unchanged for both `11111` and `22222`; `sys.dm_tran_locks` was
checked and showed no held locks; the `CaptureDeadlock_Day9` XE session
was stopped and dropped. SQL syntax for both files was additionally
checked with `SET PARSEONLY ON` against the exact files on disk.

## Limitation

As with Task 1, the two sessions were driven by two non-interactive
`sqlcmd` processes rather than two live query windows, with `WAITFOR
DELAY` used to line up the timing reliably. The locking/deadlock
behavior observed is unaffected by that — it's the same engine-level
lock manager either way — but the pause between steps in a real SSMS/ADS
session would come from switching windows by hand instead of a fixed
delay.
