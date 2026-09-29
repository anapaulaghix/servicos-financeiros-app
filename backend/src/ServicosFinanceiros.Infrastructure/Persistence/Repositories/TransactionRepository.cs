using Microsoft.EntityFrameworkCore;
using ServicosFinanceiros.Application.Abstractions;
using ServicosFinanceiros.Domain.Accounts;

namespace ServicosFinanceiros.Infrastructure.Persistence.Repositories;

internal sealed class TransactionRepository : ITransactionRepository
{
    private readonly AppDbContext _dbContext;

    public TransactionRepository(AppDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public Task<bool> ExistsAsync(Guid eventId, CancellationToken cancellationToken)
    {
        return _dbContext.Transactions.AnyAsync(t => t.EventId == eventId, cancellationToken);
    }

    public void Add(Transaction transaction)
    {
        _dbContext.Transactions.Add(transaction);
    }
}
