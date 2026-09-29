using ServicosFinanceiros.Domain.Accounts;

namespace ServicosFinanceiros.Application.Abstractions;

public interface IAccountRepository
{
    /// <summary>
    /// Carrega a conta bloqueando a linha até o fim da transação corrente, de modo que
    /// eventos concorrentes sobre a mesma conta sejam processados em série.
    /// </summary>
    Task<Account?> GetByIdForUpdateAsync(Guid accountId, CancellationToken cancellationToken);
}
