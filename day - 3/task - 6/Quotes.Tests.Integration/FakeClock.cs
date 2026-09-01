using System;
using QuotesApi.Services;

namespace Quotes.Tests.Integration;

public class FakeClock : IClock
{
    public DateTimeOffset UtcNow { get; set; } = new DateTimeOffset(2030, 1, 1, 12, 0, 0, TimeSpan.Zero);
}
