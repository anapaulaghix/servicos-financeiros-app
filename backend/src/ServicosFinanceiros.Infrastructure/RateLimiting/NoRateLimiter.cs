namespace ServicosFinanceiros.Infrastructure.RateLimiting;

/// <summary>Usado quando o Redis não está configurado (ex.: rodar a API fora do Docker): não limita nada.</summary>
internal sealed class NoRateLimiter : IRateLimiter
{
    public Task<RateLimitDecision> AcquireAsync(
        string policy,
        string partitionKey,
        int permitLimit,
        TimeSpan window,
        CancellationToken cancellationToken)
    {
        return Task.FromResult(RateLimitDecision.Allowed(permitLimit));
    }
}
