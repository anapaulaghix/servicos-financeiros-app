using ServicosFinanceiros.Domain.Accounts;

namespace ServicosFinanceiros.Application.Accounts;

/// <summary>Linha do extrato: o lançamento e o efeito dele no saldo.</summary>
public sealed record StatementEntry(
    Guid EventId,
    TransactionType Type,
    decimal Amount,
    decimal SignedAmount,
    decimal BalanceAfter,
    DateTimeOffset OccurredAt,
    DateTimeOffset ProcessedAt);
