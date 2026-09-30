using ServicosFinanceiros.Domain.Accounts;

namespace ServicosFinanceiros.Application.Transactions;

/// <summary>Resultado do processamento de um evento.</summary>
/// <param name="Transaction">O lançamento: o recém-gravado ou, num reenvio, o original.</param>
/// <param name="IsReplay">
/// O evento já tinha sido processado com os mesmos dados; nada foi gravado agora e o saldo não mudou.
/// </param>
public sealed record ProcessTransactionResult(Transaction Transaction, bool IsReplay);
