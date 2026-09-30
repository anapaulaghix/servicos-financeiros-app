namespace ServicosFinanceiros.Application.Transactions;

/// <summary>Lado de leitura dos lançamentos: devolve modelos de leitura, nunca entidades de domínio.</summary>
public interface ITransactionQueries
{
    /// <summary>Lançamento identificado pelo <paramref name="eventId"/>, ou null se não existir.</summary>
    Task<TransactionDetails?> GetAsync(Guid eventId, CancellationToken cancellationToken);
}
