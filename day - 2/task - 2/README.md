# Day 2 Task 2: async/await with cancellation through layers

## Requirements
- `CancellationToken` on every async I/O method
- Token flows from request abort signal through endpoint/service/repository/EF
- EF Core receives the token
- Avoid `Task.Run` inside async methods
- Avoid `.Result` and `.Wait()`
- Test endpoint cancellation behavior
