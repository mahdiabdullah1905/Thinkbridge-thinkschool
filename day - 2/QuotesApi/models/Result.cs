namespace QuotesApi.Models;

public record Result<T>(T? Value, string? Error)
{
    public bool IsSuccess => Error is null;
}
