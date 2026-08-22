using Dapper;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using MediatR;
using QuotesApi.Data;
using QuotesApi.Models;

namespace QuotesApi.Queries;

// Same read as GetQuoteListQueryHandler (see day - 12/task - 1), reimplemented directly
// against the database instead of through EF's LINQ translation, to compare the two on
// this query specifically (day - 12/task - 2). Not wired to an endpoint - MapQuoteEndpoints'
// GET / still dispatches GetQuoteListQuery. Reuses AppDbContext only to read the connection
// string it's already configured with, so this hits the same database, not a second one.
public class GetQuoteListDapperQueryHandler : IRequestHandler<GetQuoteListDapperQuery, PaginatedResponse<QuoteListItem>>
{
    private const int PreviewLength = 120;

    private const string CountSql = """
        SELECT COUNT(*) FROM "Quotes" WHERE "IsDeleted" = 0
        """;

    // Mirrors the shape of the EF-generated SQL for GetQuoteListQuery (see day - 12/task - 1's
    // README for the captured EF version): same WHERE, same ORDER BY, same LIMIT/OFFSET paging,
    // same correlated COUNT(*) subquery for AuthorQuoteCount.
    private const string PageSql = """
        SELECT
            q."Id" AS "Id",
            q."Author" AS "Author",
            CASE
                WHEN length(q."Text") <= @PreviewLength THEN q."Text"
                ELSE substr(q."Text", 1, @PreviewLength) || '...'
            END AS "TextPreview",
            (SELECT COUNT(*) FROM "Quotes" q0 WHERE q0."IsDeleted" = 0 AND q0."Author" = q."Author") AS "AuthorQuoteCount"
        FROM "Quotes" q
        WHERE q."IsDeleted" = 0
        ORDER BY q."Id"
        LIMIT @Size OFFSET @Offset
        """;

    private readonly AppDbContext _context;

    public GetQuoteListDapperQueryHandler(AppDbContext context)
    {
        _context = context;
    }

    public async Task<PaginatedResponse<QuoteListItem>> Handle(GetQuoteListDapperQuery request, CancellationToken cancellationToken)
    {
        var connectionString = _context.Database.GetConnectionString()
            ?? throw new InvalidOperationException("AppDbContext has no connection string configured.");

        using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        var totalCount = await connection.ExecuteScalarAsync<int>(
            new CommandDefinition(CountSql, cancellationToken: cancellationToken));

        var rows = await connection.QueryAsync<QuoteListRow>(
            new CommandDefinition(
                PageSql,
                new { PreviewLength, Size = request.Size, Offset = (request.Page - 1) * request.Size },
                cancellationToken: cancellationToken));

        return new PaginatedResponse<QuoteListItem>
        {
            Page = request.Page,
            Size = request.Size,
            TotalCount = totalCount,
            Items = rows.Select(row => new QuoteListItem((int)row.Id, row.Author, row.TextPreview, (int)row.AuthorQuoteCount)).ToList()
        };
    }

    // Matches what Microsoft.Data.Sqlite actually hands back for an INTEGER column: SQLite's
    // storage class is a dynamic, up-to-8-byte integer, so the driver reads it as long
    // regardless of declared width - it has no static type info to narrow to int the way EF's
    // LINQ translation does. Dapper's constructor-matching materializer checks the reader's
    // runtime type against QuoteListItem's constructor parameters and throws on an int/long
    // mismatch rather than silently narrowing, so the row is read as-is here and narrowed
    // explicitly below instead of asking Dapper to build QuoteListItem directly.
    private record QuoteListRow(long Id, string Author, string TextPreview, long AuthorQuoteCount);
}
