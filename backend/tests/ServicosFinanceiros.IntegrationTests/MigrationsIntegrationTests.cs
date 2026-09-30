using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using ServicosFinanceiros.Domain.Accounts;
using ServicosFinanceiros.Infrastructure.Persistence;

namespace ServicosFinanceiros.IntegrationTests;

/// <summary>
/// Migrations com SQL escrito à mão, testadas sobre um banco que já tem dados (como uma instalação existente).
/// </summary>
[Collection(PostgresCollection.Name)]
public class MigrationsIntegrationTests(PostgresFixture fixture)
{
    private const string MigrationBeforeSequence = "20260929024335_StatementIndexByProcessedAt";

    /// <summary>Banco novo e isolado no mesmo container, para migrar passo a passo sem afetar os outros testes.</summary>
    private AppDbContext NewDatabase()
    {
        var connectionString = new NpgsqlConnectionStringBuilder(fixture.ConnectionString)
        {
            Database = $"migration_{Guid.NewGuid():N}",
        }.ConnectionString;

        return new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(connectionString).Options);
    }

    [Fact]
    public async Task StatementOrderBySequence_ShouldNumberExistingHistoryInStatementOrder_AndContinueFromTheLargest()
    {
        await using var db = NewDatabase();
        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync(MigrationBeforeSequence);

        // Histórico gravado antes da coluna existir, inserido fora da ordem de processed_at: a ordem física
        // da tabela não pode decidir a numeração.
        var accountId = Guid.NewGuid();
        var (first, second, third) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"INSERT INTO accounts (id, holder_name, balance, created_at) VALUES ({accountId}, 'Titular', 25, '2026-01-01T00:00:00Z')");
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"""
            INSERT INTO transactions (event_id, account_id, type, amount, occurred_at, balance_after, processed_at) VALUES
                ({third}, {accountId}, 'Debit', 5, '2026-01-03T00:00:00Z', 25, '2026-01-03T00:00:00Z'),
                ({first}, {accountId}, 'Credit', 10, '2026-01-01T00:00:00Z', 10, '2026-01-01T00:00:00Z'),
                ({second}, {accountId}, 'Credit', 20, '2026-01-02T00:00:00Z', 30, '2026-01-02T00:00:00Z')
            """);

        await migrator.MigrateAsync();

        var numbered = await db.Transactions.AsNoTracking()
            .OrderBy(t => t.Sequence)
            .Select(t => new { t.EventId, t.Sequence })
            .ToListAsync();
        numbered.Select(t => t.EventId).Should().Equal(first, second, third);
        numbered.Select(t => t.Sequence).Should().Equal(1L, 2L, 3L);

        // Um lançamento novo recebe o próximo número, sem colidir com o histórico.
        var account = await db.Accounts.SingleAsync(a => a.Id == accountId);
        db.Transactions.Add(account.Apply(Guid.NewGuid(), TransactionType.Credit, 1m, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow));
        await db.SaveChangesAsync();

        (await db.Transactions.MaxAsync(t => t.Sequence)).Should().Be(4L);

        await db.Database.EnsureDeletedAsync();
    }
}
