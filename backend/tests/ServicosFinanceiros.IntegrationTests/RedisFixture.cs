using Testcontainers.Redis;

namespace ServicosFinanceiros.IntegrationTests;

/// <summary>Sobe um Redis real em container para os testes de rate limiting e de health check.</summary>
public sealed class RedisFixture : IAsyncLifetime
{
    private readonly RedisContainer _container = new RedisBuilder("redis:7-alpine").Build();

    public string ConnectionString => _container.GetConnectionString();

    public Task InitializeAsync() => _container.StartAsync();

    public Task DisposeAsync() => _container.DisposeAsync().AsTask();
}
