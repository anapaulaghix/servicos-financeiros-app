using Microsoft.EntityFrameworkCore;
using ServicosFinanceiros.Application.Accounts;
using ServicosFinanceiros.Domain.Accounts;

namespace ServicosFinanceiros.Infrastructure.Persistence.Repositories;

internal sealed class AccountQueries(AppDbContext dbContext) : IAccountQueries
{
    public async Task<IReadOnlyList<AccountSummary>> ListAsync(CancellationToken cancellationToken)
    {
        return await dbContext.Accounts
            .AsNoTracking()
            .OrderBy(a => a.HolderName)
            .Select(a => new AccountSummary(a.Id, a.HolderName, a.Balance, a.CreatedAt))
            .ToListAsync(cancellationToken);
    }

    public Task<AccountSummary?> GetAsync(Guid accountId, CancellationToken cancellationToken)
    {
        return dbContext.Accounts
            .AsNoTracking()
            .Where(a => a.Id == accountId)
            .Select(a => new AccountSummary(a.Id, a.HolderName, a.Balance, a.CreatedAt))
            .SingleOrDefaultAsync(cancellationToken);
    }

    public async Task<PagedResult<StatementEntry>?> GetStatementAsync(
        Guid accountId, int page, int pageSize, CancellationToken cancellationToken)
    {
        if (!await dbContext.Accounts.AnyAsync(a => a.Id == accountId, cancellationToken))
            return null;

        var entries = dbContext.Transactions
            .AsNoTracking()
            .Where(t => t.AccountId == accountId);

        var total = await entries.CountAsync(cancellationToken);

        // Em long: page * pageSize estoura int em páginas enormes (ex.: page=int.MaxValue).
        var skip = (long)(page - 1) * pageSize;
        if (skip >= total)
            return new PagedResult<StatementEntry>([], page, pageSize, total);

        var items = await entries
            // Pela sequência de gravação, e não por processed_at: com várias instâncias da API, relógios
            // defasados (ou um ajuste de NTP) inverteriam a ordem e quebrariam a cadeia de balance_after.
            .OrderByDescending(t => t.Sequence)
            .Skip((int)skip)
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
