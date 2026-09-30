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
public class ApiIntegrationTests(PostgresFixture postgres, RedisFixture redis)
{
    private const int PermitLimit = 3;
    private const string TrustedProxyIp = "172.18.0.5";

    /// <summary>
    /// Cria a API simulando que toda requisição chega de <paramref name="remoteIp"/>
    /// (no TestServer não há conexão real, então o IP é definido por um middleware de teste).
    /// </summary>
    private WebApplicationFactory<Program> CreateApi(string remoteIp) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.UseSetting("ConnectionStrings:Postgres", postgres.ConnectionString);
            builder.UseSetting("ConnectionStrings:Redis", redis.ConnectionString);
            builder.UseSetting("RateLimiting:Transactions:PermitLimit", PermitLimit.ToString(System.Globalization.CultureInfo.InvariantCulture));
            builder.UseSetting("RateLimiting:Transactions:WindowSeconds", "60");
            builder.UseSetting("ReverseProxy:TrustedProxies:0", TrustedProxyIp);
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
        // A requisição vem do nginx (proxy configurado como confiável) e o IP real do cliente está no X-Forwarded-For.
        await using var api = CreateApi(TrustedProxyIp);
        var client = api.CreateClient();

        var firstClient = RandomPublicIp();
        var secondClient = $"198.51.100.{Random.Shared.Next(1, 255)}";
        for (var i = 0; i < PermitLimit; i++)
            await PostWithForwardedFor(client, firstClient);

        (await PostWithForwardedFor(client, firstClient)).Should().Be(HttpStatusCode.TooManyRequests);
        (await PostWithForwardedFor(client, secondClient)).Should().NotBe(HttpStatusCode.TooManyRequests);
    }

    [Fact]
    public async Task DirectCallerFromUntrustedAddress_CannotSpoofForwardedForToEscapeTheLimit()
    {
        // Mesma rede privada, mas não é o proxy configurado: um X-Forwarded-For diferente a cada
        // requisição é ignorado, e o limite continua contado pelo IP de quem conectou.
        await using var api = CreateApi($"172.18.0.{Random.Shared.Next(100, 200)}");
        var client = api.CreateClient();

        for (var i = 0; i < PermitLimit; i++)
            await PostWithForwardedFor(client, RandomPublicIp());

        (await PostWithForwardedFor(client, RandomPublicIp())).Should().Be(HttpStatusCode.TooManyRequests);
    }

    private static async Task<HttpStatusCode> PostWithForwardedFor(HttpClient client, string forwardedFor)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/transactions")
        {
            Content = JsonContent.Create(UnknownAccountEvent()),
        };
        request.Headers.Add("X-Forwarded-For", forwardedFor);
        return (await client.SendAsync(request)).StatusCode;
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
