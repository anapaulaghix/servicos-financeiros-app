namespace ServicosFinanceiros.Application.Accounts;

/// <summary>Lado de leitura: consultas otimizadas que não passam pelo agregado.</summary>
public interface IAccountQueries
{
    Task<IReadOnlyList<AccountSummary>> ListAsync(CancellationToken cancellationToken);

    Task<AccountSummary?> GetAsync(Guid accountId, CancellationToken cancellationToken);

    /// <summary>
    /// Extrato paginado, do lançamento mais recente para o mais antigo (pela ordem de processamento,
    /// que é a ordem em que o saldo foi de fato alterado). Retorna null se a conta não existir.
    /// </summary>
    Task<PagedResult<StatementEntry>?> GetStatementAsync(
        Guid accountId, int page, int pageSize, CancellationToken cancellationToken);
}
