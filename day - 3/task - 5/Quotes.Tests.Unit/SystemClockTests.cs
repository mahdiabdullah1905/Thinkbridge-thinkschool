using System;
using FluentAssertions;
using QuotesApi.Services;
using Quotes.Tests.Unit.Fakes;
using Xunit;

namespace Quotes.Tests.Unit;

public class SystemClockTests
{
    [Fact]
    public void SystemClock_UtcNow_ReturnsTimeNearNow()
    {
        // Arrange
        var clock = new SystemClock();
        var before = DateTimeOffset.UtcNow;

        // Act
        var now = clock.UtcNow;

        // Assert
        var after = DateTimeOffset.UtcNow;
        now.Should().BeOnOrAfter(before).And.BeOnOrBefore(after);
    }

    [Fact]
    public void FakeClock_UtcNow_ReturnsConfiguredTime()
    {
        // Arrange
        var expectedTime = new DateTimeOffset(2025, 1, 1, 12, 0, 0, TimeSpan.Zero);
        var clock = new FakeClock { UtcNow = expectedTime };

        // Act
        var actualTime = clock.UtcNow;

        // Assert
        actualTime.Should().Be(expectedTime);
    }
}
