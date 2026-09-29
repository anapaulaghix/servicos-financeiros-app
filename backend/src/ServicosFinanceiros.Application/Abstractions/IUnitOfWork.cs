namespace ServicosFinanceiros.Application.Abstractions;

public interface IUnitOfWork
{
    /// <summary>
    /// Executa <paramref name="work"/> dentro de uma transação de banco: as alterações feitas
    /// nela são gravadas juntas ou descartadas juntas.
    /// </summary>
    /// <exception cref="UniqueConstraintViolationException">
    /// Uma restrição de unicidade foi violada ao gravar (ex.: dois eventos idênticos concorrentes).
    /// </exception>
    Task<T> ExecuteInTransactionAsync<T>(
        Func<CancellationToken, Task<T>> work,
        CancellationToken cancellationToken);
}
