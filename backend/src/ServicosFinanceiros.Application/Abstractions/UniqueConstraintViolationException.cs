namespace ServicosFinanceiros.Application.Abstractions;

/// <summary>Sinaliza, sem expor detalhes do banco, que uma restrição de unicidade foi violada.</summary>
public sealed class UniqueConstraintViolationException(Exception innerException) : Exception("Violação de restrição de unicidade.", innerException)
{
}
