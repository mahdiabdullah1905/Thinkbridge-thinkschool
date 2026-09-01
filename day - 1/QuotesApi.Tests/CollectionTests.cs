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
        collection.AddItem(10);
        
        Assert.Single(collection.Items);
        Assert.Equal(10, collection.Items.First().QuoteId);
    }

    [Fact]
    public void AddItem_DuplicateQuoteId_ThrowsInvalidOperationException()
    {
        var collection = new Collection("My Quotes", "user123");
        collection.AddItem(10);
        
        Assert.Throws<InvalidOperationException>(() => collection.AddItem(10));
    }

    [Fact]
    public void AddItem_Exceeds50Items_ThrowsInvalidOperationException()
    {
        var collection = new Collection("My Quotes", "user123");
        for (int i = 1; i <= 50; i++)
        {
            collection.AddItem(i);
        }
        
        Assert.Equal(50, collection.Items.Count);
        Assert.Throws<InvalidOperationException>(() => collection.AddItem(999));
    }

    [Fact]
    public void RemoveItem_ExistingQuoteId_RemovesAndReturnsTrue()
    {
        var collection = new Collection("My Quotes", "user123");
        collection.AddItem(10);
        
        var result = collection.RemoveItem(10);
        
        Assert.True(result);
        Assert.Empty(collection.Items);
    }

    [Fact]
    public void RemoveItem_NonExistingQuoteId_ReturnsFalse()
    {
        var collection = new Collection("My Quotes", "user123");
        var result = collection.RemoveItem(99);
        
        Assert.False(result);
    }
}
