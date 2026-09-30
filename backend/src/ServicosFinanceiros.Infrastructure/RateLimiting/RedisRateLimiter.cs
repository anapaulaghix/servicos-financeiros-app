using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace ServicosFinanceiros.Infrastructure.RateLimiting;

/// <summary>
/// Janela fixa no Redis: um contador por política e cliente, que expira junto com a janela.
/// Como o estado fica no Redis, o limite vale para todas as instâncias da API, não por processo.
/// </summary>
internal sealed partial class RedisRateLimiter(IConnectionMultiplexer redis, ILogger<RedisRateLimiter> logger) : IRateLimiter
{
    // INCR e PEXPIRE no mesmo script: a operação é atômica no Redis, então requisições simultâneas
    // nunca "perdem" incrementos nem criam um contador sem expiração.
    private static readonly LuaScript FixedWindowScript = LuaScript.Prepare(
        """
        local current = redis.call('INCR', @key)
        if current == 1 then
          redis.call('PEXPIRE', @key, @windowMs)
        end
        return { current, redis.call('PTTL', @key) }
        """);

    public async Task<RateLimitDecision> AcquireAsync(
        string policy,
        string partitionKey,
        int permitLimit,
        TimeSpan window,
        CancellationToken cancellationToken)
    {
        var key = (RedisKey)$"ratelimit:{policy}:{partitionKey}";

        try
        {
            var result = (RedisResult[])(await redis.GetDatabase().ScriptEvaluateAsync(
                FixedWindowScript,
                new { key, windowMs = (long)window.TotalMilliseconds }))!;

            var count = (long)result[0];
            var ttlMs = Math.Max(0, (long)result[1]);

            return count <= permitLimit
                ? RateLimitDecision.Allowed((int)(permitLimit - count))
                : new RateLimitDecision(false, 0, TimeSpan.FromMilliseconds(ttlMs));
        }
        catch (Exception ex) when (ex is RedisException or TimeoutException or ObjectDisposedException)
        {
            // Fail-open: Redis fora do ar não pode impedir lançamentos financeiros.
            // A indisponibilidade aparece no health check de readiness e neste log.
            LogRedisUnavailable(logger, ex, policy);
            return RateLimitDecision.Allowed(permitLimit);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Rate limiting indisponível (Redis); requisição permitida para {Policy:l}")]
    private static partial void LogRedisUnavailable(ILogger logger, Exception exception, string policy);
}
