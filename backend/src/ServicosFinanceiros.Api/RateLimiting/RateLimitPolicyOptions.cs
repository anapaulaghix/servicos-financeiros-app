using System.ComponentModel.DataAnnotations;

namespace ServicosFinanceiros.Api.RateLimiting;

public sealed class RateLimitPolicyOptions
{
    public const string SectionName = "RateLimiting";
    public const string Transactions = "Transactions";
    
    [Range(1, int.MaxValue)]
    public int PermitLimit { get; init; } = 20;

    [Range(1, 3600)]
    public int WindowSeconds { get; init; } = 10;

    public TimeSpan Window => TimeSpan.FromSeconds(WindowSeconds);
}
