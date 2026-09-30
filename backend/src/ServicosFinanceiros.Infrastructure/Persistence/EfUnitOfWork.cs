using Microsoft.EntityFrameworkCore;
using Npgsql;
using ServicosFinanceiros.Application.Abstractions;

namespace ServicosFinanceiros.Infrastructure.Persistence;

internal sealed class EfUnitOfWork(AppDbContext dbContext) : IUnitOfWork
{
    public async Task<T> ExecuteInTransactionAsync<T>(
        Func<CancellationToken, Task<T>> work,
        CancellationToken cancellationToken)
    {
        // Um único commit grava o lançamento e o saldo atualizado, ou nada é gravado.
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            var result = await work(cancellationToken);
            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return result;
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            dbContext.ChangeTracker.Clear();
            throw new UniqueConstraintViolationException(ex);
        }
        catch
        {
            // Com o rollback, o que o contexto rastreia (saldo alterado, lançamento pendente) não existe
            // no banco. Limpar evita que um SaveChanges posterior no mesmo escopo grave esse estado.
            dbContext.ChangeTracker.Clear();
            throw;
        }
    }
}
