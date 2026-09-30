namespace ServicosFinanceiros.Domain.Exceptions;

public sealed class InvalidTransactionException(string message) : DomainException(message)
{
}
