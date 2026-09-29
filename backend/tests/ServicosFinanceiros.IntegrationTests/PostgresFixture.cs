using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ServicosFinanceiros.Application;
using ServicosFinanceiros.Domain.Accounts;
using ServicosFinanceiros.Infrastructure;
using ServicosFinanceiros.Infrastructure.Persistence;
using Testcontainers.PostgreSql;

namespace ServicosFinanceiros.IntegrationTests;

/// <summary>
/// Sobe um PostgreSQL real em container, aplica as migrations e monta o mesmo grafo de
/// dependências da API (AddApplication + AddInfrastructure). Compartilhado por todos os testes.
/// </summary>
public sealed class PostgresFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:17-alpine").Build();

    public ServiceProvider Services { get; private set; } = null!;

    public string ConnectionString => _container.GetConnectionString();

    public async Task InitializeAsync()
    {
        await _container.StartAsync();

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [$"ConnectionStrings:{Infrastructure.DependencyInjection.ConnectionStringName}"] = ConnectionString
            })
            .Build();

        Services = new ServiceCollection()
            .AddLogging()
            .AddSingleton<IConfiguration>(configuration)
            .AddApplication()
            .AddInfrastructure(configuration)
            .BuildServiceProvider();

        await DatabaseInitializer.InitializeAsync(Services, seedDemoData: false);
    }

    public async Task DisposeAsync()
    {
        await Services.DisposeAsync();
        await _container.DisposeAsync();
    }

    /// <summary>Cria uma conta nova (isolando cada teste dos demais) com o saldo inicial informado.</summary>
    public async Task<Guid> CreateAccountAsync(decimal initialBalance = 0m)
    {
        await using var scope = Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var now = DateTimeOffset.UtcNow;

        var account = Account.Open(Guid.NewGuid(), "Titular de Teste", now);
        db.Accounts.Add(account);

        if (initialBalance > 0)
            db.Transactions.Add(account.Apply(Guid.NewGuid(), TransactionType.Credit, initialBalance, now, now));

        await db.SaveChangesAsync();
        return account.Id;
    }

    /// <summary>Lê o estado gravado no banco, sem passar por nenhum cache do EF.</summary>
    public async Task<(decimal Balance, List<Transaction> Transactions)> ReadAccountAsync(Guid accountId)
    {
        await using var scope = Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var balance = await db.Accounts.Where(a => a.Id == accountId).Select(a => a.Balance).SingleAsync();
        var transactions = await db.Transactions.AsNoTracking().Where(t => t.AccountId == accountId).ToListAsync();
        return (balance, transactions);
    }
}

/// <summary>Containers compartilhados por todos os testes de integração (sobem uma vez por execução).</summary>
[CollectionDefinition(Name)]
public sealed class PostgresCollection : ICollectionFixture<PostgresFixture>, ICollectionFixture<RedisFixture>
{
    public const string Name = "postgres";
}
