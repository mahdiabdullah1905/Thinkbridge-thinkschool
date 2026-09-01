# Day 2 Task 1: Dependency Injection at Depth

## Summary
- **Singleton**: `IClock` / `SystemClock`
- **Scoped**: DbContext and repositories (`AppDbContext`, `IQuoteRepository`, `ICollectionRepository`)
- **Transient**: `ExceptionHandlingMiddleware`
- Explicit DI lifetime tests verify the behavior of these lifetimes.
- Fixed/fake clock testing implemented in `CollectionTests` without coupling the aggregate to the DI container.
