using Microsoft.EntityFrameworkCore;
using ServicosFinanceiros.Application.Accounts;
using ServicosFinanceiros.Domain.Accounts;

namespace ServicosFinanceiros.Infrastructure.Persistence.Repositories;

internal sealed class AccountQueries(AppDbContext dbContext) : IAccountQueries
{
    private readonly AppDbContext _dbContext = dbContext;

    public async Task<IReadOnlyList<AccountSummary>> ListAsync(CancellationToken cancellationToken)
    {
        return await _dbContext.Accounts
            .AsNoTracking()
            .OrderBy(a => a.HolderName)
            .Select(a => new AccountSummary(a.Id, a.HolderName, a.Balance, a.CreatedAt))
            .ToListAsync(cancellationToken);
    }

    public Task<AccountSummary?> GetAsync(Guid accountId, CancellationToken cancellationToken)
    {
        return _dbContext.Accounts
            .AsNoTracking()
            .Where(a => a.Id == accountId)
            .Select(a => new AccountSummary(a.Id, a.HolderName, a.Balance, a.CreatedAt))
            .SingleOrDefaultAsync(cancellationToken);
    }

    public async Task<PagedResult<StatementEntry>?> GetStatementAsync(
        Guid accountId, int page, int pageSize, CancellationToken cancellationToken)
    {
        if (!await _dbContext.Accounts.AnyAsync(a => a.Id == accountId, cancellationToken))
            return null;

        var entries = _dbContext.Transactions
            .AsNoTracking()
            .Where(t => t.AccountId == accountId);

        var total = await entries.CountAsync(cancellationToken);

        var items = await entries
            .OrderByDescending(t => t.ProcessedAt)
            .ThenByDescending(t => t.EventId) // desempate estável para a paginação
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(t => new StatementEntry(
                t.EventId,
                t.Type,
                t.Amount,
                t.Type == TransactionType.Credit ? t.Amount : -t.Amount,
                t.BalanceAfter,
                t.OccurredAt,
                t.ProcessedAt))
            .ToListAsync(cancellationToken);

        return new PagedResult<StatementEntry>(items, page, pageSize, total);
    }
}
