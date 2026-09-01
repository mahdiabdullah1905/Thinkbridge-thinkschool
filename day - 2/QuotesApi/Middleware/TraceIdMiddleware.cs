using Microsoft.AspNetCore.Http;
using Serilog.Context;
using System.Threading.Tasks;

namespace QuotesApi.Middleware;

public class TraceIdMiddleware : IMiddleware
{
    public async Task InvokeAsync(HttpContext context, RequestDelegate next)
    {
        var traceId = System.Diagnostics.Activity.Current?.TraceId.ToHexString() ?? context.TraceIdentifier;
        using (LogContext.PushProperty("TraceId", traceId))
        {
            await next(context);
        }
    }
}
