namespace ServicosFinanceiros.Domain.Exceptions;

public sealed class InsufficientFundsException : DomainException
{
    public InsufficientFundsException(Guid accountId, decimal balance, decimal requestedAmount)
        : base($"Saldo insuficiente na conta {accountId}: saldo {balance:F2}, débito solicitado {requestedAmount:F2}.")
    {
        AccountId = accountId;
        Balance = balance;
        RequestedAmount = requestedAmount;
    }

    public Guid AccountId { get; }
    public decimal Balance { get; }
    public decimal RequestedAmount { get; }
}
