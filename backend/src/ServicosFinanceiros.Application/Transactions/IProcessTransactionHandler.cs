namespace ServicosFinanceiros.Application.Transactions;

public interface IProcessTransactionHandler
{
    Task<ProcessTransactionResult> HandleAsync(ProcessTransactionCommand command, CancellationToken cancellationToken);
}
