using Microsoft.EntityFrameworkCore;
using ServicosFinanceiros.Application.Transactions;
using ServicosFinanceiros.Domain.Accounts;

namespace ServicosFinanceiros.Infrastructure.Persistence.Repositories;

internal sealed class TransactionQueries(AppDbContext dbContext) : ITransactionQueries
{
    private readonly AppDbContext _dbContext = dbContext;

    public Task<Transaction?> GetAsync(Guid eventId, CancellationToken cancellationToken)
    {
        return _dbContext.Transactions
            .AsNoTracking()
            .SingleOrDefaultAsync(t => t.EventId == eventId, cancellationToken);
    }
}
