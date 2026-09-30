namespace ServicosFinanceiros.Domain.Exceptions;

public sealed class InsufficientFundsException(Guid accountId, decimal balance, decimal requestedAmount) : DomainException($"Saldo insuficiente na conta {accountId}: saldo {balance:F2}, débito solicitado {requestedAmount:F2}.")
{
    public Guid AccountId { get; } = accountId;
    public decimal Balance { get; } = balance;
    public decimal RequestedAmount { get; } = requestedAmount;
}
