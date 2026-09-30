namespace ServicosFinanceiros.Application.Accounts;

/// <summary>Lado de leitura: consultas otimizadas que não passam pelo agregado.</summary>
public interface IAccountQueries
{
    Task<IReadOnlyList<AccountSummary>> ListAsync(CancellationToken cancellationToken);

    Task<AccountSummary?> GetAsync(Guid accountId, CancellationToken cancellationToken);

    Task<PagedResult<StatementEntry>?> GetStatementAsync(
        Guid accountId, int page, int pageSize, CancellationToken cancellationToken);
}
