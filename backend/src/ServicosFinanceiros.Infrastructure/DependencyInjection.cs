using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ServicosFinanceiros.Application.Abstractions;
using ServicosFinanceiros.Application.Accounts;
using ServicosFinanceiros.Application.Transactions;
using ServicosFinanceiros.Infrastructure.HealthChecks;
using ServicosFinanceiros.Infrastructure.Persistence;
using ServicosFinanceiros.Infrastructure.Persistence.Repositories;
using ServicosFinanceiros.Infrastructure.RateLimiting;
using StackExchange.Redis;

namespace ServicosFinanceiros.Infrastructure;

public static class DependencyInjection
{
    public const string ConnectionStringName = "Postgres";
    public const string RedisConnectionStringName = "Redis";

    /// <summary>Tag dos health checks que decidem se a instância pode receber tráfego (readiness).</summary>
    public const string ReadinessTag = "ready";

    /// <summary>
    /// Registra persistência (EF Core/PostgreSQL), as implementações das portas da aplicação,
    /// o rate limiting (Redis, opcional) e os health checks das dependências.
    /// </summary>
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString(ConnectionStringName)
            ?? throw new InvalidOperationException(
                $"Connection string '{ConnectionStringName}' não configurada. Fora do Docker, defina-a por user secrets " +
                $"(ConnectionStrings:{ConnectionStringName}) ou pela variável de ambiente ConnectionStrings__{ConnectionStringName}. " +
                "Passo a passo em backend/README.md.");

        services.AddDbContext<AppDbContext>(options => options.UseNpgsql(connectionString));

        services.AddScoped<IAccountRepository, AccountRepository>();
        services.AddScoped<ITransactionRepository, TransactionRepository>();
        services.AddScoped<IUnitOfWork, EfUnitOfWork>();
        services.AddScoped<IAccountQueries, AccountQueries>();
        services.AddScoped<ITransactionQueries, TransactionQueries>();
        services.AddSingleton(TimeProvider.System);

        var healthChecks = services.AddHealthChecks()
            .AddDbContextCheck<AppDbContext>("postgres", tags: [ReadinessTag]);

        var redisConnectionString = configuration.GetConnectionString(RedisConnectionStringName);
        if (string.IsNullOrWhiteSpace(redisConnectionString))
        {
            services.AddSingleton<IRateLimiter, NoRateLimiter>();
        }
        else
        {
            services.AddSingleton<IConnectionMultiplexer>(_ =>
            {
                var options = ConfigurationOptions.Parse(redisConnectionString);
                options.AbortOnConnectFail = false;
                return ConnectionMultiplexer.Connect(options);
            });
            services.AddSingleton<IRateLimiter, RedisRateLimiter>();
            healthChecks.AddCheck<RedisHealthCheck>("redis", tags: [ReadinessTag]);
        }

        return services;
    }
}
