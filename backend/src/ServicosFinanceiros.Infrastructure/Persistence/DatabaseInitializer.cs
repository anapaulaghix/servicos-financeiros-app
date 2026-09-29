using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ServicosFinanceiros.Domain.Accounts;

namespace ServicosFinanceiros.Infrastructure.Persistence;

public static class DatabaseInitializer
{
    /// <summary>
    /// Aplica as migrations pendentes e, opcionalmente, cria contas de demonstração.
    /// Pensado para ambientes de desenvolvimento e Docker; em produção, prefira aplicar
    /// migrations como uma etapa separada do deploy.
    /// </summary>
    public static async Task InitializeAsync(
        IServiceProvider services,
        bool seedDemoData,
        CancellationToken cancellationToken = default)
    {
        await using var scope = services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var timeProvider = scope.ServiceProvider.GetRequiredService<TimeProvider>();

        await dbContext.Database.MigrateAsync(cancellationToken);

        if (seedDemoData)
            await SeedAsync(dbContext, timeProvider.GetUtcNow(), cancellationToken);
    }

    private static async Task SeedAsync(AppDbContext dbContext, DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (await dbContext.Accounts.AnyAsync(cancellationToken))
            return;

        // Cada conta nasce com um crédito inicial registrado como lançamento,
        // para que o saldo já reflita o histórico desde o primeiro dia.
        var demoAccounts = new (Guid Id, string Holder, decimal InitialCredit)[]
        {
            (Guid.Parse("11111111-1111-1111-1111-111111111111"), "Ana Paula Ghis", 1500.00m),
            (Guid.Parse("22222222-2222-2222-2222-222222222222"), "Carlos Eduardo Lima", 320.50m),
            (Guid.Parse("33333333-3333-3333-3333-333333333333"), "Mariana Souza", 0m)
        };

        foreach (var (id, holder, initialCredit) in demoAccounts)
        {
            var account = Account.Open(id, holder, now);
            dbContext.Accounts.Add(account);

            if (initialCredit > 0)
            {
                var transaction = account.Apply(
                    Guid.NewGuid(), TransactionType.Credit, initialCredit, now, now);
                dbContext.Transactions.Add(transaction);
            }
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
