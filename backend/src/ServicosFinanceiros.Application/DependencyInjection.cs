using Microsoft.Extensions.DependencyInjection;
using ServicosFinanceiros.Application.Transactions;

namespace ServicosFinanceiros.Application;

public static class DependencyInjection
{
    /// <summary>Registra os casos de uso da camada de aplicação.</summary>
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddScoped<IProcessTransactionHandler, ProcessTransactionHandler>();
        return services;
    }
}
