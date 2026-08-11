using System;
using FluentAssertions;
using QuotesApi.Models;
using Xunit;

namespace Tests.Domain;

public class CollectionTests
{
    [Fact]
    public void EmptyName_ThrowsArgumentException()
    {
        Action act = () => new Collection("", "owner123");

        act.Should().Throw<ArgumentException>()
           .WithMessage("*Name cannot be empty*");
    }

    [Fact]
    public void NameGreaterThan80Characters_ThrowsArgumentException()
    {
        var longName = new string('a', 81);
        Action act = () => new Collection(longName, "owner123");

        act.Should().Throw<ArgumentException>()
           .WithMessage("*Name must be between 3 and 80 characters long*");
    }

    [Fact]
    public void Adding51stItem_ThrowsInvalidOperationException()
    {
        var collection = new Collection("My Quotes", "owner123");
        for (int i = 1; i <= 50; i++)
        {
            collection.AddItem(i, DateTimeOffset.UtcNow);
        }

        Action act = () => collection.AddItem(999, DateTimeOffset.UtcNow);

        act.Should().Throw<InvalidOperationException>()
           .WithMessage("*cannot have more than 50 items*");
    }

    [Fact]
    public void AddingDuplicateQuoteId_ThrowsInvalidOperationException()
    {
        var collection = new Collection("My Quotes", "owner123");
        collection.AddItem(1, DateTimeOffset.UtcNow);

        Action act = () => collection.AddItem(1, DateTimeOffset.UtcNow);

        act.Should().Throw<InvalidOperationException>()
           .WithMessage("*is already in the collection*");
    }

    [Fact]
    public void RemovingNonExistentQuoteId_ThrowsInvalidOperationException()
    {
        var collection = new Collection("My Quotes", "owner123");

        Action act = () => collection.RemoveItem(1);

        act.Should().Throw<InvalidOperationException>()
           .WithMessage("*is not in the collection*");
    }

    [Fact]
    public void AddingAndThenRemovingItem_LeavesZeroItems()
    {
        var collection = new Collection("My Quotes", "owner123");
        collection.AddItem(1, DateTimeOffset.UtcNow);
        
        collection.RemoveItem(1);

        collection.Items.Should().BeEmpty();
    }
}
