using Microsoft.Extensions.Diagnostics.HealthChecks;
using StackExchange.Redis;

namespace ServicosFinanceiros.Infrastructure.HealthChecks;

/// <summary>
/// Redis fora do ar não impede lançamentos (o rate limiting falha aberto), então o estado é
/// Degraded, não Unhealthy: a instância continua apta a receber tráfego.
/// </summary>
internal sealed class RedisHealthCheck(IConnectionMultiplexer redis) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var latency = await redis.GetDatabase().PingAsync();
            return HealthCheckResult.Healthy($"Latência {latency.TotalMilliseconds:F0} ms");
        }
        catch (Exception ex) when (ex is RedisException or TimeoutException)
        {
            return HealthCheckResult.Degraded("Redis indisponível; rate limiting desativado (fail-open).", ex);
        }
    }
}
