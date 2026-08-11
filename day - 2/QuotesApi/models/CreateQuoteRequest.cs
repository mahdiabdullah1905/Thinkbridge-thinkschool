using System.ComponentModel.DataAnnotations;

namespace QuotesApi.Models;

public class CreateQuoteRequest
{
    [Required]
    [StringLength(100, MinimumLength = 1)]
    public string Author { get; set; } = "";

    [Required]
    [StringLength(1000, MinimumLength = 1)]
    public string Text { get; set; } = "";
}
