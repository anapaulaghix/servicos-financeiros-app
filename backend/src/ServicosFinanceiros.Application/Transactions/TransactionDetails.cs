using ServicosFinanceiros.Domain.Accounts;

namespace ServicosFinanceiros.Application.Transactions;

/// <summary>Modelo de leitura de um lançamento: projeção do banco, sem carregar a entidade de domínio.</summary>
public sealed record TransactionDetails(
    Guid EventId,
    Guid AccountId,
    TransactionType Type,
    decimal Amount,
    decimal BalanceAfter,
    DateTimeOffset OccurredAt,
    DateTimeOffset ProcessedAt);
