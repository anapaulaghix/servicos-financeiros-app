namespace ServicosFinanceiros.Domain.Exceptions;

public sealed class DuplicateEventException(Guid eventId) : DomainException($"O evento {eventId} já foi processado.")
{
    public Guid EventId { get; } = eventId;
}
