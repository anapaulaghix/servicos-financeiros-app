namespace ServicosFinanceiros.Infrastructure.RateLimiting;

/// <param name="IsAllowed">A requisição cabe no limite da janela atual.</param>
/// <param name="RetryAfter">Tempo até a janela reiniciar (só relevante quando bloqueada).</param>
public sealed record RateLimitDecision(bool IsAllowed, int Remaining, TimeSpan RetryAfter)
{
    public static RateLimitDecision Allowed(int remaining) => new(true, remaining, TimeSpan.Zero);
}

/// <summary>Limitador de requisições por janela fixa, compartilhado entre instâncias da API.</summary>
public interface IRateLimiter
{
    /// <summary>
    /// Conta uma requisição para <paramref name="partitionKey"/> (ex.: IP do cliente) dentro da janela.
    /// Se o armazenamento estiver indisponível, deve permitir a requisição (fail-open).
    /// </summary>
    Task<RateLimitDecision> AcquireAsync(
        string policy,
        string partitionKey,
        int permitLimit,
        TimeSpan window,
        CancellationToken cancellationToken);
}
