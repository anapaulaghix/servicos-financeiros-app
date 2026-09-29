using Microsoft.Extensions.Logging;
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
    private readonly ILogger<ProcessTransactionHandler> _logger;

    public ProcessTransactionHandler(
        IAccountRepository accounts,
        ITransactionRepository transactions,
        IUnitOfWork unitOfWork,
        TimeProvider timeProvider,
        ILogger<ProcessTransactionHandler> logger)
    {
        _accounts = accounts;
        _transactions = transactions;
        _unitOfWork = unitOfWork;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task<Transaction> HandleAsync(
        ProcessTransactionCommand command,
        CancellationToken cancellationToken)
    {
        try
        {
            var transaction = await _unitOfWork.ExecuteInTransactionAsync(
                ct => ProcessAsync(command, ct),
                cancellationToken);

            // Registrado só depois do commit: o log nunca afirma um lançamento que não foi gravado.
            _logger.LogInformation(
                "Lançamento {EventId} processado: {TransactionType:l} de {Amount} na conta {AccountId}; saldo após {BalanceAfter}",
                transaction.EventId, transaction.Type, transaction.Amount, transaction.AccountId, transaction.BalanceAfter);

            return transaction;
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
