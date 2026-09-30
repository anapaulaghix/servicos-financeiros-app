using Microsoft.Extensions.Logging;
using ServicosFinanceiros.Application.Abstractions;
using ServicosFinanceiros.Domain.Accounts;
using ServicosFinanceiros.Domain.Exceptions;

namespace ServicosFinanceiros.Application.Transactions;

public sealed partial class ProcessTransactionHandler(
    IAccountRepository accounts,
    ITransactionRepository transactions,
    IUnitOfWork unitOfWork,
    TimeProvider timeProvider,
    ILogger<ProcessTransactionHandler> logger) : IProcessTransactionHandler
{
    /// <summary>
    /// Processa o evento uma única vez. Um reenvio com os mesmos dados devolve o lançamento original
    /// (<see cref="ProcessTransactionResult.IsReplay"/>), para que quem perdeu a resposta receba o resultado;
    /// o mesmo <c>eventId</c> com outros dados é recusado com <see cref="DuplicateEventException"/>.
    /// </summary>
    public async Task<ProcessTransactionResult> HandleAsync(
        ProcessTransactionCommand command,
        CancellationToken cancellationToken)
    {
        // Caminho rápido para reenvios: responde sem abrir transação nem bloquear a conta.
        var existing = await transactions.FindAsync(command.EventId, cancellationToken);
        if (existing is not null)
            return Replay(existing, command);

        try
        {
            var transaction = await unitOfWork.ExecuteInTransactionAsync(
                ct => ProcessAsync(command, ct),
                cancellationToken);

            LogProcessed(logger, transaction.EventId, transaction.Type, transaction.Amount, transaction.AccountId, transaction.BalanceAfter);

            return new ProcessTransactionResult(transaction, IsReplay: false);
        }
        catch (UniqueConstraintViolationException)
        {
            // Rede de proteção da idempotência: requisições concorrentes com o mesmo eventId passam
            // juntas pela consulta acima, mas só uma consegue gravar (PK em eventId). As demais leem
            // o lançamento vencedor, já confirmado, e seguem a mesma regra de um reenvio comum.
            var winner = await transactions.FindAsync(command.EventId, cancellationToken)
                         ?? throw new DuplicateEventException(command.EventId);
            return Replay(winner, command);
        }
    }

    private async Task<Transaction> ProcessAsync(ProcessTransactionCommand command, CancellationToken ct)
    {
        var account = await accounts.GetByIdForUpdateAsync(command.AccountId, ct)
                      ?? throw new AccountNotFoundException(command.AccountId);

        var transaction = account.Apply(
            command.EventId,
            command.Type,
            command.Amount,
            ToStoredPrecision(command.OccurredAt),
            ToStoredPrecision(timeProvider.GetUtcNow()));

        transactions.Add(transaction);
        return transaction;
    }

    private ProcessTransactionResult Replay(Transaction existing, ProcessTransactionCommand command)
    {
        if (!existing.IsSameEvent(command.AccountId, command.Type, command.Amount, command.OccurredAt))
            throw new DuplicateEventException(command.EventId);

        LogReplayed(logger, existing.EventId, existing.AccountId);
        return new ProcessTransactionResult(existing, IsReplay: true);
    }

    // O banco guarda datas em microssegundos; o DateTimeOffset tem ticks de 100 ns. Gerar o lançamento já
    // nessa precisão faz a resposta do 201 ser idêntica ao que foi gravado e ao que um reenvio devolve.
    private static DateTimeOffset ToStoredPrecision(DateTimeOffset value) =>
        value.AddTicks(-(value.Ticks % TimeSpan.TicksPerMicrosecond));

    // Registrado só depois do commit: o log nunca afirma um lançamento que não foi gravado.
    [LoggerMessage(Level = LogLevel.Information,
        Message = "Lançamento {EventId} processado: {TransactionType} de {Amount} na conta {AccountId}; saldo após {BalanceAfter}")]
    private static partial void LogProcessed(
        ILogger logger, Guid eventId, TransactionType transactionType, decimal amount, Guid accountId, decimal balanceAfter);

    [LoggerMessage(Level = LogLevel.Information,
        Message = "Evento {EventId} reenviado com os mesmos dados; devolvido o lançamento original da conta {AccountId}, sem novo lançamento")]
    private static partial void LogReplayed(ILogger logger, Guid eventId, Guid accountId);
}
