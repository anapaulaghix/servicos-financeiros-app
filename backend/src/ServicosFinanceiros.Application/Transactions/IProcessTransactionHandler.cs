using ServicosFinanceiros.Domain.Accounts;

namespace ServicosFinanceiros.Application.Transactions;

public interface IProcessTransactionHandler
{
    Task<Transaction> HandleAsync(ProcessTransactionCommand command, CancellationToken cancellationToken);
}
