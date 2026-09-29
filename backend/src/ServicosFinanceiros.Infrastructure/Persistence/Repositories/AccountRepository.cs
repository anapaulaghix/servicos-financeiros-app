using Microsoft.EntityFrameworkCore;
using ServicosFinanceiros.Application.Abstractions;
using ServicosFinanceiros.Domain.Accounts;

namespace ServicosFinanceiros.Infrastructure.Persistence.Repositories;

internal sealed class AccountRepository : IAccountRepository
{
    private readonly AppDbContext _dbContext;

    public AccountRepository(AppDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public Task<Account?> GetByIdForUpdateAsync(Guid accountId, CancellationToken cancellationToken)
    {
        // FOR UPDATE bloqueia a linha até o commit/rollback, serializando eventos da mesma conta.
        return _dbContext.Accounts
            .FromSqlInterpolated($"SELECT * FROM accounts WHERE id = {accountId} FOR UPDATE")
            .SingleOrDefaultAsync(cancellationToken);
    }
}
