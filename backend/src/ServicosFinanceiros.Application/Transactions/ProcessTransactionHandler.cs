using ServicosFinanceiros.Application.Abstractions;
using ServicosFinanceiros.Domain.Accounts;
using ServicosFinanceiros.Domain.Exceptions;

namespace ServicosFinanceiros.Application.Transactions;

public sealed class ProcessTransactionHandler : IProcessTransactionHandler
{
    private readonly IAccountRepository _accounts;
    private readonly ITransactionRepository _transactions;
    private readonly IUnitOfWork _unitOfWork;
    private readonly TimeProvider _timeProvider;

    public ProcessTransactionHandler(
        IAccountRepository accounts,
        ITransactionRepository transactions,
        IUnitOfWork unitOfWork,
        TimeProvider timeProvider)
    {
        _accounts = accounts;
        _transactions = transactions;
        _unitOfWork = unitOfWork;
        _timeProvider = timeProvider;
    }

    public async Task<Transaction> HandleAsync(
        ProcessTransactionCommand command,
        CancellationToken cancellationToken)
    {
        try
        {
            return await _unitOfWork.ExecuteInTransactionAsync(
                ct => ProcessAsync(command, ct),
                cancellationToken);
        }
        catch (UniqueConstraintViolationException)
        {
            // Rede de proteção da idempotência: dois eventos idênticos concorrentes passam
            // pela checagem em ExistsAsync, mas só um consegue gravar (PK em eventId).
            throw new DuplicateEventException(command.EventId);
        }
    }

    private async Task<Transaction> ProcessAsync(ProcessTransactionCommand command, CancellationToken ct)
    {
        if (await _transactions.ExistsAsync(command.EventId, ct))
            throw new DuplicateEventException(command.EventId);

        var account = await _accounts.GetByIdForUpdateAsync(command.AccountId, ct)
                      ?? throw new AccountNotFoundException(command.AccountId);

        var transaction = account.Apply(
            command.EventId,
            command.Type,
            command.Amount,
            command.OccurredAt,
            _timeProvider.GetUtcNow());

        _transactions.Add(transaction);
        return transaction;
    }
}
