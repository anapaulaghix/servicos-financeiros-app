namespace ServicosFinanceiros.Application.Accounts;

public sealed record AccountSummary(
    Guid Id,
    string HolderName,
    decimal Balance,
    DateTimeOffset CreatedAt);
