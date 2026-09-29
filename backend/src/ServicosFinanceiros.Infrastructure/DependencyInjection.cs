using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ServicosFinanceiros.Application.Abstractions;
using ServicosFinanceiros.Application.Accounts;
using ServicosFinanceiros.Infrastructure.Persistence;
using ServicosFinanceiros.Infrastructure.Persistence.Repositories;

namespace ServicosFinanceiros.Infrastructure;

public static class DependencyInjection
{
    public const string ConnectionStringName = "Postgres";

    /// <summary>Registra persistência (EF Core/PostgreSQL) e as implementações das portas da aplicação.</summary>
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString(ConnectionStringName)
            ?? throw new InvalidOperationException(
                $"Connection string '{ConnectionStringName}' não configurada.");

        services.AddDbContext<AppDbContext>(options => options.UseNpgsql(connectionString));

        services.AddScoped<IAccountRepository, AccountRepository>();
        services.AddScoped<ITransactionRepository, TransactionRepository>();
        services.AddScoped<IUnitOfWork, EfUnitOfWork>();
        services.AddScoped<IAccountQueries, AccountQueries>();
        services.AddSingleton(TimeProvider.System);

        return services;
    }
}
