using Microsoft.AspNetCore.Http;
using Serilog.Context;
using System.Threading.Tasks;

namespace QuotesApi.Middleware;

public class TraceIdMiddleware : IMiddleware
{
    public async Task InvokeAsync(HttpContext context, RequestDelegate next)
    {
        // Push the existing TraceIdentifier to the LogContext as "TraceId"
        using (LogContext.PushProperty("TraceId", context.TraceIdentifier))
        {
            await next(context);
        }
    }
}
