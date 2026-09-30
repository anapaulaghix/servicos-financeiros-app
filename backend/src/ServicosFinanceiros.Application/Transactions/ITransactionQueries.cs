using ServicosFinanceiros.Domain.Accounts;

namespace ServicosFinanceiros.Application.Transactions;

/// <summary>Lado de leitura dos lançamentos.</summary>
public interface ITransactionQueries
{
    /// <summary>Lançamento identificado pelo <paramref name="eventId"/>, ou null se não existir.</summary>
    Task<Transaction?> GetAsync(Guid eventId, CancellationToken cancellationToken);
}
