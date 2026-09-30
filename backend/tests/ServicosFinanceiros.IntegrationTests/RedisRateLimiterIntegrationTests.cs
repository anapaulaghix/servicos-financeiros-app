using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using ServicosFinanceiros.Infrastructure;
using ServicosFinanceiros.Infrastructure.RateLimiting;

namespace ServicosFinanceiros.IntegrationTests;

/// <summary>Rate limiter contra um Redis real, resolvido pelo mesmo registro de IoC da API.</summary>
[Collection(PostgresCollection.Name)]
public class RedisRateLimiterIntegrationTests(PostgresFixture postgres, RedisFixture redis)
{
    private readonly PostgresFixture _postgres = postgres;
    private readonly RedisFixture _redis = redis;

    private ServiceProvider BuildServices(string? redisConnectionString)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Postgres"] = _postgres.ConnectionString,
                ["ConnectionStrings:Redis"] = redisConnectionString,
            })
            .Build();

        return new ServiceCollection()
            .AddLogging()
            .AddSingleton<IConfiguration>(configuration)
            .AddInfrastructure(configuration)
            .BuildServiceProvider();
    }

    /// <summary>Cada teste usa um cliente próprio, para os contadores no Redis não se misturarem.</summary>
    private static string NewClient() => $"cliente-{Guid.NewGuid():N}";

    [Fact]
    public async Task AllowsUpToTheLimitThenBlocksWithRetryAfter()
    {
        await using var services = BuildServices(_redis.ConnectionString);
        var limiter = services.GetRequiredService<IRateLimiter>();
        var client = NewClient();
        var window = TimeSpan.FromSeconds(30);

        var decisions = new List<RateLimitDecision>();
        for (var i = 0; i < 4; i++)
            decisions.Add(await limiter.AcquireAsync("test", client, 3, window, CancellationToken.None));

        decisions.Select(d => d.IsAllowed).Should().Equal(true, true, true, false);
        decisions.Take(3).Select(d => d.Remaining).Should().Equal(2, 1, 0);
        decisions[3].RetryAfter.Should().BeGreaterThan(TimeSpan.Zero).And.BeLessThanOrEqualTo(window);
    }

    [Fact]
    public async Task EachClientHasItsOwnLimit()
    {
        await using var services = BuildServices(_redis.ConnectionString);
        var limiter = services.GetRequiredService<IRateLimiter>();
        var first = NewClient();
        var second = NewClient();

        await limiter.AcquireAsync("test", first, 1, TimeSpan.FromSeconds(30), CancellationToken.None);
        var firstAgain = await limiter.AcquireAsync("test", first, 1, TimeSpan.FromSeconds(30), CancellationToken.None);
        var secondClient = await limiter.AcquireAsync("test", second, 1, TimeSpan.FromSeconds(30), CancellationToken.None);

        firstAgain.IsAllowed.Should().BeFalse();
        secondClient.IsAllowed.Should().BeTrue();
    }

    [Fact]
    public async Task WindowExpirationReleasesTheClient()
    {
        await using var services = BuildServices(_redis.ConnectionString);
        var limiter = services.GetRequiredService<IRateLimiter>();
        var client = NewClient();
        var window = TimeSpan.FromMilliseconds(500);

        await limiter.AcquireAsync("test", client, 1, window, CancellationToken.None);
        (await limiter.AcquireAsync("test", client, 1, window, CancellationToken.None)).IsAllowed.Should().BeFalse();

        await Task.Delay(TimeSpan.FromMilliseconds(800));

        (await limiter.AcquireAsync("test", client, 1, window, CancellationToken.None)).IsAllowed.Should().BeTrue();
    }

    [Fact]
    public async Task ConcurrentRequestsNeverExceedTheLimit()
    {
        // O script Lua é atômico no Redis: 50 requisições simultâneas contra um limite de 10 liberam exatamente 10.
        await using var services = BuildServices(_redis.ConnectionString);
        var limiter = services.GetRequiredService<IRateLimiter>();
        var client = NewClient();

        var decisions = await Task.WhenAll(Enumerable.Range(0, 50).Select(_ =>
            limiter.AcquireAsync("test", client, 10, TimeSpan.FromSeconds(30), CancellationToken.None)));

        decisions.Count(d => d.IsAllowed).Should().Be(10);
    }

    [Fact]
    public async Task RedisUnavailable_AllowsRequests_AndReadinessReportsDegraded()
    {
        // Porta sem Redis: o limitador falha aberto e o health check aponta o problema.
        await using var services = BuildServices("127.0.0.1:1,connectTimeout=300,syncTimeout=300,asyncTimeout=300");
        var limiter = services.GetRequiredService<IRateLimiter>();

        var decision = await limiter.AcquireAsync("test", NewClient(), 1, TimeSpan.FromSeconds(30), CancellationToken.None);
        var report = await services.GetRequiredService<HealthCheckService>().CheckHealthAsync();

        decision.IsAllowed.Should().BeTrue();
        report.Entries["redis"].Status.Should().Be(HealthStatus.Degraded);
        report.Entries["postgres"].Status.Should().Be(HealthStatus.Healthy);
    }

    [Fact]
    public async Task WithoutRedisConfigured_RateLimitingIsDisabled()
    {
        await using var services = BuildServices(redisConnectionString: null);
        var limiter = services.GetRequiredService<IRateLimiter>();
        var client = NewClient();

        await limiter.AcquireAsync("test", client, 1, TimeSpan.FromSeconds(30), CancellationToken.None);
        var second = await limiter.AcquireAsync("test", client, 1, TimeSpan.FromSeconds(30), CancellationToken.None);

        second.IsAllowed.Should().BeTrue();
    }
}
