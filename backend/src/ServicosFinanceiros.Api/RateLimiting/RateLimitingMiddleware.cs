using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using ServicosFinanceiros.Infrastructure.RateLimiting;

namespace ServicosFinanceiros.Api.RateLimiting;

/// <summary>
/// Aplica o <see cref="RateLimitAttribute"/> dos endpoints. O particionamento é por IP do cliente
/// (o IP real chega via X-Forwarded-For, tratado pelo UseForwardedHeaders antes deste middleware).
/// Acima do limite responde 429 com ProblemDetails e o cabeçalho Retry-After.
/// </summary>
public sealed class RateLimitingMiddleware
{
    private readonly RequestDelegate _next;

    public RateLimitingMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(
        HttpContext context,
        IRateLimiter rateLimiter,
        IOptionsMonitor<RateLimitPolicyOptions> policies,
        IProblemDetailsService problemDetails,
        ILogger<RateLimitingMiddleware> logger)
    {
        var attribute = context.GetEndpoint()?.Metadata.GetMetadata<RateLimitAttribute>();
        if (attribute is null)
        {
            await _next(context);
            return;
        }

        var policy = policies.Get(attribute.Policy);
        var clientKey = context.Connection.RemoteIpAddress?.ToString() ?? "desconhecido";

        var decision = await rateLimiter.AcquireAsync(
            attribute.Policy, clientKey, policy.PermitLimit, policy.Window, context.RequestAborted);

        context.Response.Headers["RateLimit-Limit"] = policy.PermitLimit.ToString(CultureInfo.InvariantCulture);
        context.Response.Headers["RateLimit-Remaining"] = decision.Remaining.ToString(CultureInfo.InvariantCulture);

        if (decision.IsAllowed)
        {
            await _next(context);
            return;
        }

        var retryAfterSeconds = Math.Max(1, (int)Math.Ceiling(decision.RetryAfter.TotalSeconds));
        logger.LogWarning(
            "Limite de requisições excedido na política {Policy:l} pelo cliente {ClientIp:l}; nova tentativa em {RetryAfterSeconds}s",
            attribute.Policy, clientKey, retryAfterSeconds);

        context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
        context.Response.Headers.RetryAfter = retryAfterSeconds.ToString(CultureInfo.InvariantCulture);

        await problemDetails.WriteAsync(new ProblemDetailsContext
        {
            HttpContext = context,
            ProblemDetails = new ProblemDetails
            {
                Status = StatusCodes.Status429TooManyRequests,
                Title = "Muitas requisições",
                Detail = $"Limite de {policy.PermitLimit} requisições a cada {policy.WindowSeconds}s excedido. " +
                         $"Tente novamente em {retryAfterSeconds}s.",
            },
        });
    }
}
