using ServicosFinanceiros.Domain.Accounts;

namespace ServicosFinanceiros.Api.Contracts;

public sealed record TransactionResponse(
    Guid EventId,
    Guid AccountId,
    TransactionType Type,
    decimal Amount,
    decimal BalanceAfter,
    DateTimeOffset OccurredAt,
    DateTimeOffset ProcessedAt)
{
    public static TransactionResponse From(Transaction transaction) => new(
        transaction.EventId,
        transaction.AccountId,
        transaction.Type,
        transaction.Amount,
        transaction.BalanceAfter,
        transaction.OccurredAt,
        transaction.ProcessedAt);
}
