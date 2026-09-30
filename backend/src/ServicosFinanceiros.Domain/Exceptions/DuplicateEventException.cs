namespace ServicosFinanceiros.Domain.Exceptions;

/// <summary>
/// Um <c>eventId</c> já processado chegou com dados diferentes (conta, tipo, valor ou data).
/// Um reenvio com os mesmos dados não é erro: devolve o lançamento original.
/// </summary>
public sealed class DuplicateEventException(Guid eventId) : DomainException(
    $"O evento {eventId} já foi processado com outros dados. Um eventId não pode ser reutilizado em outro lançamento.")
{
    public Guid EventId { get; } = eventId;
}
