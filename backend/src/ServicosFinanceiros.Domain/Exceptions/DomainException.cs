namespace ServicosFinanceiros.Domain.Exceptions;

/// <summary>Violação de uma regra de negócio do domínio.</summary>
public abstract class DomainException : Exception
{
    protected DomainException(string message) : base(message)
    {
    }
}
