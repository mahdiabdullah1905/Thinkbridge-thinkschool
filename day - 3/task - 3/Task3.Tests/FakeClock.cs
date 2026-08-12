using QuotesApi.Services;
namespace Task3.Tests;

public class FakeClock : IClock
{
    public DateTimeOffset UtcNow { get; set; } = DateTimeOffset.UtcNow;
}
