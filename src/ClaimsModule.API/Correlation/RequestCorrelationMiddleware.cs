using System.Diagnostics;

namespace ClaimsModule.API.Correlation;

internal sealed class RequestCorrelationMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context)
    {
        var correlationId = Guid.TryParseExact(Activity.Current?.TraceId.ToString(), "N", out var id)
            && id != Guid.Empty ? id : Guid.NewGuid();

        context.Features.Set(new RequestCorrelation(correlationId));
        context.Response.OnStarting(() =>
        {
            context.Response.Headers["X-Correlation-ID"] = correlationId.ToString("D");
            return Task.CompletedTask;
        });
        await next(context);
    }
}

internal sealed record RequestCorrelation(Guid Id)
{
    public static Guid Read(HttpContext context) =>
        (context.Features.Get<RequestCorrelation>() ?? throw new InvalidOperationException("Missing request correlation")).Id;
}
