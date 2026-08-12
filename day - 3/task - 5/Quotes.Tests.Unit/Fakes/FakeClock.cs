using System;
using QuotesApi.Services;

namespace Quotes.Tests.Unit.Fakes;

public class FakeClock : IClock
{
    public DateTimeOffset UtcNow { get; set; } = DateTimeOffset.UtcNow;
}
