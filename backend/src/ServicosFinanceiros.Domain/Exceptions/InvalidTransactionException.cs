namespace ServicosFinanceiros.Domain.Exceptions;

public sealed class InvalidTransactionException : DomainException
{
    public InvalidTransactionException(string message) : base(message)
    {
    }
}
