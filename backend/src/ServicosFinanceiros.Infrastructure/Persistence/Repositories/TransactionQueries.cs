using Microsoft.EntityFrameworkCore;
using ServicosFinanceiros.Application.Transactions;

namespace ServicosFinanceiros.Infrastructure.Persistence.Repositories;

internal sealed class TransactionQueries(AppDbContext dbContext) : ITransactionQueries
{
    public Task<TransactionDetails?> GetAsync(Guid eventId, CancellationToken cancellationToken)
    {
        return dbContext.Transactions
            .AsNoTracking()
            .Where(t => t.EventId == eventId)
            .Select(t => new TransactionDetails(
                t.EventId, t.AccountId, t.Type, t.Amount, t.BalanceAfter, t.OccurredAt, t.ProcessedAt))
            .SingleOrDefaultAsync(cancellationToken);
    }
}
