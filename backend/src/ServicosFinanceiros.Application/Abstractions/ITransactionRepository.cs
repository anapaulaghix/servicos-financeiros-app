using ServicosFinanceiros.Domain.Accounts;

namespace ServicosFinanceiros.Application.Abstractions;

public interface ITransactionRepository
{
    Task<bool> ExistsAsync(Guid eventId, CancellationToken cancellationToken);

    void Add(Transaction transaction);
}
