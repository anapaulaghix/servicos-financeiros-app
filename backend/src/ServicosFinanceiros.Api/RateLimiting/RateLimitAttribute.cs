namespace ServicosFinanceiros.Api.RateLimiting;

/// <summary>
/// Marca um endpoint para ser limitado pela política informada (configurada em <c>RateLimiting:{policy}</c>).
/// Aplicado no endpoint, e não globalmente, para limitar só o que tem custo ou risco, como lançamentos.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class RateLimitAttribute(string policy) : Attribute
{
    public string Policy { get; } = policy;
}
