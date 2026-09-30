using Microsoft.Extensions.Logging;
using ServicosFinanceiros.Application.Abstractions;
using ServicosFinanceiros.Domain.Accounts;
using ServicosFinanceiros.Domain.Exceptions;

namespace ServicosFinanceiros.Application.Transactions;

public sealed class ProcessTransactionHandler(
    IAccountRepository accounts,
    ITransactionRepository transactions,
    IUnitOfWork unitOfWork,
    TimeProvider timeProvider,
    ILogger<ProcessTransactionHandler> logger) : IProcessTransactionHandler
{
    private readonly IAccountRepository _accounts = accounts;
    private readonly ITransactionRepository _transactions = transactions;
    private readonly IUnitOfWork _unitOfWork = unitOfWork;
    private readonly TimeProvider _timeProvider = timeProvider;
    private readonly ILogger<ProcessTransactionHandler> _logger = logger;

    public async Task<Transaction> HandleAsync(
        ProcessTransactionCommand command,
        CancellationToken cancellationToken)
    {
        try
        {
            var transaction = await _unitOfWork.ExecuteInTransactionAsync(
                ct => ProcessAsync(command, ct),
                cancellationToken);

            if (_logger.IsEnabled(LogLevel.Information))
            {
                _logger.LogInformation(
                    "Lançamento {EventId} processado: {TransactionType:l} de {Amount} na conta {AccountId}; saldo após {BalanceAfter}",
                    transaction.EventId, transaction.Type, transaction.Amount, transaction.AccountId, transaction.BalanceAfter);
            }

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
