using ServicosFinanceiros.Domain.Exceptions;

namespace ServicosFinanceiros.Domain.Accounts;

/// <summary>
/// Agregado raiz: concentra o saldo consolidado e as regras que o protegem.
/// O saldo só muda através de <see cref="Apply"/>, que devolve o lançamento correspondente
/// para que histórico e saldo sejam persistidos juntos, na mesma transação.
/// </summary>
public sealed class Account
{
    public const int MoneyScale = 2;

    /// <summary>Maior valor representável em <c>numeric(18,2)</c>, a precisão usada no banco.</summary>
    public const decimal MaxMoneyValue = 9_999_999_999_999_999.99m;

    // Construtor sem parâmetros para materialização pelo EF Core.
    private Account()
    {
        HolderName = string.Empty;
    }

    private Account(Guid id, string holderName, DateTimeOffset createdAt)
    {
        Id = id;
        HolderName = holderName;
        Balance = 0m;
        CreatedAt = createdAt;
    }

    public Guid Id { get; private set; }
    public string HolderName { get; private set; }
    public decimal Balance { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    public static Account Open(Guid id, string holderName, DateTimeOffset createdAt)
    {
        if (id == Guid.Empty)
            throw new InvalidAccountException("O identificador da conta é obrigatório.");

        if (string.IsNullOrWhiteSpace(holderName))
            throw new InvalidAccountException("O nome do titular é obrigatório.");

        return new Account(id, holderName.Trim(), createdAt);
    }

    /// <summary>
    /// Aplica um evento financeiro à conta, atualizando o saldo e devolvendo o lançamento gerado.
    /// A verificação de duplicidade do <paramref name="eventId"/> é responsabilidade da camada
    /// de aplicação/persistência, pois depende do histórico armazenado.
    /// </summary>
    /// <exception cref="InvalidTransactionException">Dados do evento inválidos.</exception>
    /// <exception cref="InsufficientFundsException">Débito maior que o saldo disponível.</exception>
    public Transaction Apply(
        Guid eventId,
        TransactionType type,
        decimal amount,
        DateTimeOffset occurredAt,
        DateTimeOffset processedAt)
    {
        Validate(eventId, type, amount);

        if (type == TransactionType.Debit && amount > Balance)
            throw new InsufficientFundsException(Id, Balance, amount);

        // Sem esta regra, o crédito seria aceito aqui e só falharia no banco (estouro de numeric(18,2)).
        if (type == TransactionType.Credit && amount > MaxMoneyValue - Balance)
            throw new InvalidTransactionException("O crédito faria o saldo ultrapassar o limite permitido.");

        Balance += type == TransactionType.Credit ? amount : -amount;

        return new Transaction(eventId, Id, type, amount, occurredAt, Balance, processedAt);
    }

    private static void Validate(Guid eventId, TransactionType type, decimal amount)
    {
        if (eventId == Guid.Empty)
            throw new InvalidTransactionException("O identificador do evento é obrigatório.");

        if (!Enum.IsDefined(type))
            throw new InvalidTransactionException("O tipo da transação deve ser CREDIT ou DEBIT.");

        if (amount <= 0)
            throw new InvalidTransactionException("O valor da transação deve ser maior que zero.");

        if (amount > MaxMoneyValue)
            throw new InvalidTransactionException("O valor da transação ultrapassa o limite permitido.");

        if (decimal.Round(amount, MoneyScale) != amount)
            throw new InvalidTransactionException($"O valor da transação admite no máximo {MoneyScale} casas decimais.");
    }
}
