using ServicosFinanceiros.Domain.Accounts;

namespace ServicosFinanceiros.Application.Transactions;

/// <summary>Evento financeiro de entrada, já com o contrato validado pela borda (API).</summary>
public sealed record ProcessTransactionCommand(
    Guid EventId,
    Guid AccountId,
    TransactionType Type,
    decimal Amount,
    DateTimeOffset OccurredAt);
