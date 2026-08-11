namespace QuotesApi.Models;

public class Collection
{
    public int Id { get; private set; }
    public string Name { get; private set; } = null!;
    public string OwnerId { get; private set; } = null!;

    private readonly List<CollectionItem> _items = new();
    public IReadOnlyCollection<CollectionItem> Items => _items.AsReadOnly();

    // Required by EF Core
    private Collection() { }

    public Collection(string name, string ownerId)
    {
        ValidateName(name);
        if (string.IsNullOrWhiteSpace(ownerId))
            throw new ArgumentException("OwnerId cannot be empty.", nameof(ownerId));

        Name = name.Trim();
        OwnerId = ownerId;
    }

    public void ChangeName(string newName)
    {
        ValidateName(newName);
        Name = newName.Trim();
    }

    public void AddItem(int quoteId, DateTimeOffset addedAt)
    {
        if (_items.Count >= 50)
        {
            throw new InvalidOperationException("A collection cannot have more than 50 items.");
        }

        if (_items.Any(i => i.QuoteId == quoteId))
        {
            throw new InvalidOperationException($"Quote {quoteId} is already in the collection.");
        }

        _items.Add(new CollectionItem(quoteId, addedAt));
    }

    public bool RemoveItem(int quoteId)
    {
        var item = _items.FirstOrDefault(i => i.QuoteId == quoteId);
        if (item == null)
        {
            return false;
        }

        return _items.Remove(item);
    }

    private static void ValidateName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Name cannot be empty.");

        var trimmed = name.Trim();
        if (trimmed.Length < 3 || trimmed.Length > 80)
            throw new ArgumentException("Name must be between 3 and 80 characters long.");
    }
}
