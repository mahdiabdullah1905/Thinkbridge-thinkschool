using System.ComponentModel.DataAnnotations;

namespace QuotesApi.Models;

public class CreateCollectionRequest
{
    [Required]
    [StringLength(80, MinimumLength = 3)]
    public string Name { get; set; } = null!;

    [Required]
    public string OwnerId { get; set; } = null!;
}

public class AddQuoteToCollectionRequest
{
    [Required]
    public int QuoteId { get; set; }
}
