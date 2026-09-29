namespace ServicosFinanceiros.Domain.Exceptions;

public sealed class AccountNotFoundException : DomainException
{
    public AccountNotFoundException(Guid accountId)
        : base($"Conta {accountId} não encontrada.")
    {
        AccountId = accountId;
    }

    public Guid AccountId { get; }
}
