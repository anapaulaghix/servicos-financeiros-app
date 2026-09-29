namespace ServicosFinanceiros.Domain.Exceptions;

public sealed class DuplicateEventException : DomainException
{
    public DuplicateEventException(Guid eventId)
        : base($"O evento {eventId} já foi processado.")
    {
        EventId = eventId;
    }

    public Guid EventId { get; }
}
