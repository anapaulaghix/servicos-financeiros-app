namespace ServicosFinanceiros.Domain.Accounts;

/// <summary>
/// Lançamento imutável no histórico de uma conta. O <see cref="EventId"/> é a chave de
/// idempotência: um mesmo evento nunca gera dois lançamentos.
/// </summary>
public sealed class Transaction
{
    // Construtor sem parâmetros para materialização pelo EF Core.
    private Transaction()
    {
    }

    internal Transaction(
        Guid eventId,
        Guid accountId,
        TransactionType type,
        decimal amount,
        DateTimeOffset occurredAt,
        decimal balanceAfter,
        DateTimeOffset processedAt)
    {
        EventId = eventId;
        AccountId = accountId;
        Type = type;
        Amount = amount;
        OccurredAt = occurredAt;
        BalanceAfter = balanceAfter;
        ProcessedAt = processedAt;
    }

    public Guid EventId { get; private set; }
    public Guid AccountId { get; private set; }
    public TransactionType Type { get; private set; }
    public decimal Amount { get; private set; }
    public DateTimeOffset OccurredAt { get; private set; }
    public decimal BalanceAfter { get; private set; }
    public DateTimeOffset ProcessedAt { get; private set; }

    /// <summary>Impacto do lançamento no saldo: positivo para crédito, negativo para débito.</summary>
    public decimal SignedAmount => Type == TransactionType.Credit ? Amount : -Amount;
}
