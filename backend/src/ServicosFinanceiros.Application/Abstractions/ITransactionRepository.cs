using ServicosFinanceiros.Domain.Accounts;

namespace ServicosFinanceiros.Application.Abstractions;

public interface ITransactionRepository
{
    /// <summary>
    /// Lançamento já gravado com o <paramref name="eventId"/>, lido do banco (nunca de alterações
    /// pendentes no contexto), ou null se não existir.
    /// </summary>
    Task<Transaction?> FindAsync(Guid eventId, CancellationToken cancellationToken);

    void Add(Transaction transaction);
}
