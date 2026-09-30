using Microsoft.EntityFrameworkCore;
using ServicosFinanceiros.Application.Abstractions;
using ServicosFinanceiros.Domain.Accounts;

namespace ServicosFinanceiros.Infrastructure.Persistence.Repositories;

internal sealed class TransactionRepository(AppDbContext dbContext) : ITransactionRepository
{
    public Task<Transaction?> FindAsync(Guid eventId, CancellationToken cancellationToken)
    {
        // AsNoTracking: o valor vem sempre do banco. Uma consulta rastreada devolveria a instância já
        // presente no contexto (ex.: o lançamento que acabou de falhar ao gravar), e não a confirmada.
        return dbContext.Transactions
            .AsNoTracking()
            .SingleOrDefaultAsync(t => t.EventId == eventId, cancellationToken);
    }

    public void Add(Transaction transaction)
    {
        dbContext.Transactions.Add(transaction);
    }
}
