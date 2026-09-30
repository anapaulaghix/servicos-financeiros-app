namespace ServicosFinanceiros.Domain.Exceptions;

/// <summary>Dados inválidos para abrir uma conta.</summary>
public sealed class InvalidAccountException(string message) : DomainException(message)
{
}
