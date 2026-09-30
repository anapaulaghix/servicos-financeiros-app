using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using ServicosFinanceiros.Application.Abstractions;
using ServicosFinanceiros.Application.Accounts;
using ServicosFinanceiros.Application.Transactions;
using ServicosFinanceiros.Domain.Accounts;
using ServicosFinanceiros.Domain.Exceptions;

namespace ServicosFinanceiros.IntegrationTests;

[Collection(PostgresCollection.Name)]
public class AccountQueriesIntegrationTests(PostgresFixture fixture)
{
    private async Task ProcessAsync(Guid accountId, TransactionType type, decimal amount)
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var handler = scope.ServiceProvider.GetRequiredService<IProcessTransactionHandler>();
        await handler.HandleAsync(
            new ProcessTransactionCommand(Guid.NewGuid(), accountId, type, amount, DateTimeOffset.UtcNow),
            CancellationToken.None);
    }

    /// <summary>
    /// Processa com um relógio próprio, como faria outra instância da API (com o relógio dela).
    /// </summary>
    private async Task ProcessWithClockAsync(Guid accountId, TransactionType type, decimal amount, TimeProvider clock)
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var services = scope.ServiceProvider;
        var handler = new ProcessTransactionHandler(
            services.GetRequiredService<IAccountRepository>(),
            services.GetRequiredService<ITransactionRepository>(),
            services.GetRequiredService<IUnitOfWork>(),
            clock,
            NullLogger<ProcessTransactionHandler>.Instance);

        await handler.HandleAsync(
            new ProcessTransactionCommand(Guid.NewGuid(), accountId, type, amount, DateTimeOffset.UtcNow),
            CancellationToken.None);
    }

    /// <summary>Extrato completo, do lançamento mais antigo para o mais recente.</summary>
    private async Task<List<StatementEntry>> FullStatementOldestFirstAsync(Guid accountId)
    {
        var statement = await QueryAsync(q => q.GetStatementAsync(accountId, 1, 100, CancellationToken.None));
        return statement!.Items.Reverse().ToList();
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private async Task<T> QueryAsync<T>(Func<IAccountQueries, Task<T>> query)
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        return await query(scope.ServiceProvider.GetRequiredService<IAccountQueries>());
    }

    [Fact]
    public async Task List_ShouldReturnAccountsWithCurrentBalance()
    {
        var accountId = await fixture.CreateAccountAsync(100m);
        await ProcessAsync(accountId, TransactionType.Debit, 30.5m);

        var accounts = await QueryAsync(q => q.ListAsync(CancellationToken.None));

        accounts.Should().ContainSingle(a => a.Id == accountId).Which.Balance.Should().Be(69.5m);
    }

    [Fact]
    public async Task Get_UnknownAccount_ShouldReturnNull()
    {
        var account = await QueryAsync(q => q.GetAsync(Guid.NewGuid(), CancellationToken.None));

        account.Should().BeNull();
    }

    [Fact]
    public async Task Statement_UnknownAccount_ShouldReturnNull()
    {
        var statement = await QueryAsync(q => q.GetStatementAsync(Guid.NewGuid(), 1, 10, CancellationToken.None));

        statement.Should().BeNull();
    }

    [Fact]
    public async Task Statement_ShouldBeNewestFirstAndShowImpactOnBalance()
    {
        var accountId = await fixture.CreateAccountAsync(100m);
        await ProcessAsync(accountId, TransactionType.Debit, 40m);   // saldo 60
        await ProcessAsync(accountId, TransactionType.Credit, 15m);  // saldo 75

        var statement = await QueryAsync(q => q.GetStatementAsync(accountId, 1, 10, CancellationToken.None));

        statement!.Items.Select(i => i.BalanceAfter).Should().Equal(75m, 60m, 100m);
        statement.Items.Select(i => i.SignedAmount).Should().Equal(15m, -40m, 100m);
        statement.Items[1].Type.Should().Be(TransactionType.Debit);
    }

    [Fact]
    public async Task Statement_ShouldPaginateWithoutRepeatingOrSkippingItems()
    {
        var accountId = await fixture.CreateAccountAsync(0m);
        for (var i = 0; i < 7; i++)
            await ProcessAsync(accountId, TransactionType.Credit, 10m);

        var first = await QueryAsync(q => q.GetStatementAsync(accountId, 1, 3, CancellationToken.None));
        var second = await QueryAsync(q => q.GetStatementAsync(accountId, 2, 3, CancellationToken.None));
        var third = await QueryAsync(q => q.GetStatementAsync(accountId, 3, 3, CancellationToken.None));

        first!.TotalItems.Should().Be(7);
        first.TotalPages.Should().Be(3);
        first.Items.Should().HaveCount(3);
        second!.Items.Should().HaveCount(3);
        third!.Items.Should().HaveCount(1);

        var allIds = first.Items.Concat(second.Items).Concat(third.Items).Select(i => i.EventId).ToList();
        allIds.Should().OnlyHaveUniqueItems();
        first.Items.Select(i => i.BalanceAfter).Should().Equal(70m, 60m, 50m);
        third.Items.Single().BalanceAfter.Should().Be(10m);
    }

    [Fact]
    public async Task Statement_PageBeyondTheEnd_ShouldReturnEmptyItemsButKeepTotals()
    {
        var accountId = await fixture.CreateAccountAsync(50m);

        var statement = await QueryAsync(q => q.GetStatementAsync(accountId, 5, 10, CancellationToken.None));

        statement!.Items.Should().BeEmpty();
        statement.TotalItems.Should().Be(1);
    }

    [Fact]
    public async Task Statement_BalanceAfterChain_ShouldMatchTheSignedAmounts_AfterConcurrentActivity()
    {
        // Cada linha do extrato precisa "fechar" com a anterior: saldo após = saldo anterior + impacto.
        // É o que evidencia o impacto de cada lançamento e prova que a ordem mostrada é a ordem real.
        var accountId = await fixture.CreateAccountAsync(100m);

        await Task.WhenAll(Enumerable.Range(0, 20).Select(async i =>
        {
            try
            {
                var type = i % 3 == 0 ? TransactionType.Credit : TransactionType.Debit;
                await ProcessAsync(accountId, type, 5m + i);
            }
            catch (InsufficientFundsException)
            {
                // Esperado em alguns débitos; o que importa é a cadeia de saldos.
            }
        }));

        var entries = await FullStatementOldestFirstAsync(accountId);
        var account = await QueryAsync(q => q.GetAsync(accountId, CancellationToken.None));

        var running = 0m;
        foreach (var entry in entries)
        {
            running += entry.SignedAmount;
            entry.BalanceAfter.Should().Be(running, "o saldo após cada lançamento deve seguir do anterior");
        }
        running.Should().Be(account!.Balance);
    }

    [Fact]
    public async Task Statement_ShouldFollowTheOrderOfRecording_EvenWhenInstanceClocksDisagree()
    {
        // Três instâncias da API com relógios defasados: cada lançamento seguinte é gravado com um
        // processed_at mais antigo. Ordenar pelo relógio inverteria o extrato e quebraria a cadeia de saldos.
        var accountId = await fixture.CreateAccountAsync(0m);
        var now = DateTimeOffset.UtcNow;
        await ProcessWithClockAsync(accountId, TransactionType.Credit, 10m, new FixedClock(now));
        await ProcessWithClockAsync(accountId, TransactionType.Credit, 20m, new FixedClock(now.AddMinutes(-1)));
        await ProcessWithClockAsync(accountId, TransactionType.Debit, 5m, new FixedClock(now.AddMinutes(-2)));

        var statement = await QueryAsync(q => q.GetStatementAsync(accountId, 1, 10, CancellationToken.None));

        statement!.Items.Select(i => i.SignedAmount).Should().Equal(-5m, 20m, 10m);
        statement.Items.Select(i => i.BalanceAfter).Should().Equal(25m, 30m, 10m);
    }
}
