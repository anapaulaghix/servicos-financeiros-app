using ServicosFinanceiros.Application.Transactions;
using ServicosFinanceiros.Domain.Accounts;

namespace ServicosFinanceiros.Api.Contracts;

/// <summary>
/// Lançamento no contrato HTTP. Fica na Api, e não é o modelo de leitura da Application, porque o mesmo
/// formato responde a dois lados: o POST (a entidade recém-gravada) e o GET (a projeção da consulta).
/// </summary>
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

    public static TransactionResponse From(TransactionDetails details) => new(
        details.EventId,
        details.AccountId,
        details.Type,
        details.Amount,
        details.BalanceAfter,
        details.OccurredAt,
        details.ProcessedAt);
}
