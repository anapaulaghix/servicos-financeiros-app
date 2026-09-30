namespace ServicosFinanceiros.Domain.Accounts;

/// <summary>
/// Lançamento imutável no histórico de uma conta. O <see cref="EventId"/> é a chave de
/// idempotência: um mesmo evento nunca gera dois lançamentos.
/// </summary>
public sealed class Transaction
{
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

    /// <summary>
    /// Posição do lançamento na ordem de gravação, atribuída pelo banco ao inserir (0 até lá). É o que
    /// ordena o extrato: lançamentos de uma mesma conta são gravados em série (bloqueio da linha da conta),
    /// então essa é exatamente a ordem em que o saldo mudou, sem depender do relógio de nenhuma instância.
    /// </summary>
    public long Sequence { get; private set; }

    /// <summary>Impacto do lançamento no saldo: positivo para crédito, negativo para débito.</summary>
    public decimal SignedAmount => Type == TransactionType.Credit ? Amount : -Amount;

    /// <summary>
    /// Indica se um evento recebido com o mesmo <see cref="EventId"/> descreve este lançamento, ou seja,
    /// se é um reenvio legítimo (ex.: retry após timeout) e não a reutilização do identificador com outros dados.
    /// </summary>
    public bool IsSameEvent(Guid accountId, TransactionType type, decimal amount, DateTimeOffset occurredAt) =>
        AccountId == accountId
        && Type == type
        && Amount == amount
        && ToMicroseconds(OccurredAt) == ToMicroseconds(occurredAt);

    // O timestamptz do PostgreSQL guarda microssegundos; o DateTimeOffset tem ticks de 100 ns. Sem truncar,
    // um reenvio idêntico com 7 casas nos segundos seria tomado como evento diferente.
    private static long ToMicroseconds(DateTimeOffset value) => value.UtcTicks / TimeSpan.TicksPerMicrosecond;
}
