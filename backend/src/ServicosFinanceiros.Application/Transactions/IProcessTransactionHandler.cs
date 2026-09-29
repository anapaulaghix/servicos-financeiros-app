using ServicosFinanceiros.Domain.Accounts;

namespace ServicosFinanceiros.Application.Transactions;

public interface IProcessTransactionHandler
{
    /// <exception cref="Domain.Exceptions.DuplicateEventException">O evento já foi processado.</exception>
    /// <exception cref="Domain.Exceptions.AccountNotFoundException">A conta não existe.</exception>
    /// <exception cref="Domain.Exceptions.InsufficientFundsException">Débito acima do saldo.</exception>
    /// <exception cref="Domain.Exceptions.InvalidTransactionException">Dados do evento inválidos.</exception>
    Task<Transaction> HandleAsync(ProcessTransactionCommand command, CancellationToken cancellationToken);
}
