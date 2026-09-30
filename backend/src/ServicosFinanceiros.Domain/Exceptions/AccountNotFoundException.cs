namespace ServicosFinanceiros.Domain.Exceptions;

public sealed class AccountNotFoundException(Guid accountId) : DomainException($"Conta {accountId} não encontrada.")
{
    public Guid AccountId { get; } = accountId;
}
