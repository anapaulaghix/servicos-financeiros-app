using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace ServicosFinanceiros.IntegrationTests;

/// <summary>
/// A API inteira em memória (WebApplicationFactory), com PostgreSQL e Redis reais: valida o pipeline
/// HTTP de ponta a ponta — health checks, rate limiting e o IP do cliente vindo do proxy.
/// </summary>
[Collection(PostgresCollection.Name)]
public class ApiIntegrationTests
{
    private const int PermitLimit = 3;

    private readonly PostgresFixture _postgres;
    private readonly RedisFixture _redis;

    public ApiIntegrationTests(PostgresFixture postgres, RedisFixture redis)
    {
        _postgres = postgres;
        _redis = redis;
    }

    /// <summary>
    /// Cria a API simulando que toda requisição chega de <paramref name="remoteIp"/>
    /// (no TestServer não há conexão real, então o IP é definido por um middleware de teste).
    /// </summary>
    private WebApplicationFactory<Program> CreateApi(string remoteIp) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.UseSetting("ConnectionStrings:Postgres", _postgres.ConnectionString);
            builder.UseSetting("ConnectionStrings:Redis", _redis.ConnectionString);
            builder.UseSetting("RateLimiting:Transactions:PermitLimit", PermitLimit.ToString());
            builder.UseSetting("RateLimiting:Transactions:WindowSeconds", "60");
            builder.ConfigureServices(services =>
                services.AddSingleton<IStartupFilter>(new FixedRemoteIpStartupFilter(IPAddress.Parse(remoteIp))));
        });

    /// <summary>IP público aleatório (faixa de documentação 203.0.113.0/24) para isolar os contadores de cada teste.</summary>
    private static string RandomPublicIp() => $"203.0.113.{Random.Shared.Next(1, 255)}";

    private static object UnknownAccountEvent() => new
    {
        eventId = Guid.NewGuid(),
        accountId = Guid.NewGuid(),
        type = "CREDIT",
        amount = 10,
        occurredAt = DateTimeOffset.UtcNow,
    };

    [Fact]
    public async Task Liveness_RespondsHealthyWithoutCheckingDependencies()
    {
        await using var api = CreateApi(RandomPublicIp());

        var response = await api.CreateClient().GetAsync("/health/live");
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        body.GetProperty("status").GetString().Should().Be("Healthy");
        body.GetProperty("checks").EnumerateObject().Should().BeEmpty();
    }

    [Fact]
    public async Task Readiness_ChecksPostgresAndRedis()
    {
        await using var api = CreateApi(RandomPublicIp());

        var response = await api.CreateClient().GetAsync("/health/ready");
        var checks = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("checks");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        checks.GetProperty("postgres").GetProperty("status").GetString().Should().Be("Healthy");
        checks.GetProperty("redis").GetProperty("status").GetString().Should().Be("Healthy");
    }

    [Fact]
    public async Task Transactions_AboveTheLimit_Returns429WithRetryAfterAndProblemDetails()
    {
        await using var api = CreateApi(RandomPublicIp());
        var client = api.CreateClient();

        // Dentro do limite a requisição chega ao controller (aqui, 404: conta inexistente).
        for (var i = 0; i < PermitLimit; i++)
        {
            var allowed = await client.PostAsJsonAsync("/api/transactions", UnknownAccountEvent());
            allowed.StatusCode.Should().Be(HttpStatusCode.NotFound);
        }

        var blocked = await client.PostAsJsonAsync("/api/transactions", UnknownAccountEvent());
        var problem = await blocked.Content.ReadFromJsonAsync<JsonElement>();

        blocked.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        blocked.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
        blocked.Headers.RetryAfter!.Delta.Should().BeGreaterThan(TimeSpan.Zero);
        blocked.Headers.GetValues("RateLimit-Remaining").Should().Equal("0");
        problem.GetProperty("status").GetInt32().Should().Be(429);
    }

    [Fact]
    public async Task ReadEndpoints_AreNotRateLimited()
    {
        await using var api = CreateApi(RandomPublicIp());
        var client = api.CreateClient();

        for (var i = 0; i < PermitLimit * 3; i++)
        {
            var response = await client.GetAsync("/api/accounts");
            response.StatusCode.Should().Be(HttpStatusCode.OK);
        }
    }

    [Fact]
    public async Task BehindTrustedProxy_LimitIsPerForwardedClientIp()
    {
        // A requisição vem do nginx (rede privada do Docker) e o IP real do cliente está no X-Forwarded-For.
        await using var api = CreateApi("172.18.0.5");
        var client = api.CreateClient();

        async Task<HttpStatusCode> PostFrom(string clientIp)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "/api/transactions")
            {
                Content = JsonContent.Create(UnknownAccountEvent()),
            };
            request.Headers.Add("X-Forwarded-For", clientIp);
            return (await client.SendAsync(request)).StatusCode;
        }

        var firstClient = RandomPublicIp();
        var secondClient = $"198.51.100.{Random.Shared.Next(1, 255)}";
        for (var i = 0; i < PermitLimit; i++)
            await PostFrom(firstClient);

        (await PostFrom(firstClient)).Should().Be(HttpStatusCode.TooManyRequests);
        (await PostFrom(secondClient)).Should().NotBe(HttpStatusCode.TooManyRequests);
    }

    private sealed class FixedRemoteIpStartupFilter(IPAddress remoteIp) : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use((HttpContext context, RequestDelegate nextMiddleware) =>
            {
                context.Connection.RemoteIpAddress = remoteIp;
                return nextMiddleware(context);
            });
            next(app);
        };
    }
}
