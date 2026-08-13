using QuotesApi.Models;

namespace QuotesApi.Tests;

public class CollectionTests
{
    [Fact]
    public void Constructor_ValidInputs_CreatesCollection()
    {
        var collection = new Collection("My Quotes", "user123");
        Assert.Equal("My Quotes", collection.Name);
        Assert.Equal("user123", collection.OwnerId);
        Assert.Empty(collection.Items);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("ab")] // Less than 3
    [InlineData("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")] // More than 80
    public void Constructor_InvalidName_ThrowsArgumentException(string invalidName)
    {
        Assert.Throws<ArgumentException>(() => new Collection(invalidName, "user123"));
    }

    [Fact]
    public void AddItem_ValidQuote_AddsToCollection()
    {
        var collection = new Collection("My Quotes", "user123");
        collection.AddItem(10, DateTimeOffset.UtcNow);
        
        Assert.Single(collection.Items);
        Assert.Equal(10, collection.Items.First().QuoteId);
    }

    [Fact]
    public void AddItem_DuplicateQuoteId_ThrowsInvalidOperationException()
    {
        var collection = new Collection("My Quotes", "user123");
        collection.AddItem(10, DateTimeOffset.UtcNow);
        
        Assert.Throws<InvalidOperationException>(() => collection.AddItem(10, DateTimeOffset.UtcNow));
    }

    [Fact]
    public void AddItem_Exceeds50Items_ThrowsInvalidOperationException()
    {
        var collection = new Collection("My Quotes", "user123");
        for (int i = 1; i <= 50; i++)
        {
            collection.AddItem(i, DateTimeOffset.UtcNow);
        }
        
        Assert.Equal(50, collection.Items.Count);
        Assert.Throws<InvalidOperationException>(() => collection.AddItem(999, DateTimeOffset.UtcNow));
    }

    [Fact]
    public void RemoveItem_ExistingQuoteId_RemovesAndReturnsTrue()
    {
        var collection = new Collection("My Quotes", "user123");
        collection.AddItem(10, DateTimeOffset.UtcNow);
        
        var result = collection.RemoveItem(10);
        
        Assert.True(result);
        Assert.Empty(collection.Items);
    }

    [Fact]
    public void RemoveItem_NonExistingQuoteId_ThrowsInvalidOperationException()
    {
        var collection = new Collection("My Quotes", "user123");
        
        Assert.Throws<InvalidOperationException>(() => collection.RemoveItem(99));
    }

    [Fact]
    public void AddItem_ShouldRecordExactTimestamp()
    {
        var collection = new Collection("Test Collection", "user123");
        var fixedTime = new DateTimeOffset(2025, 1, 1, 12, 0, 0, TimeSpan.Zero);
        
        collection.AddItem(42, fixedTime);
        
        var item = collection.Items.First();
        Assert.Equal(42, item.QuoteId);
        Assert.Equal(fixedTime, item.AddedAt);
    }
}
