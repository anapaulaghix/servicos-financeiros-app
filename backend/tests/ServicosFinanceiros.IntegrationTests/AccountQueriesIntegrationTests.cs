using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using ServicosFinanceiros.Application.Accounts;
using ServicosFinanceiros.Application.Transactions;
using ServicosFinanceiros.Domain.Accounts;

namespace ServicosFinanceiros.IntegrationTests;

[Collection(PostgresCollection.Name)]
public class AccountQueriesIntegrationTests
{
    private readonly PostgresFixture _fixture;

    public AccountQueriesIntegrationTests(PostgresFixture fixture)
    {
        _fixture = fixture;
    }

    private async Task ProcessAsync(Guid accountId, TransactionType type, decimal amount)
    {
        await using var scope = _fixture.Services.CreateAsyncScope();
        var handler = scope.ServiceProvider.GetRequiredService<IProcessTransactionHandler>();
        await handler.HandleAsync(
            new ProcessTransactionCommand(Guid.NewGuid(), accountId, type, amount, DateTimeOffset.UtcNow),
            CancellationToken.None);
    }

    private async Task<T> QueryAsync<T>(Func<IAccountQueries, Task<T>> query)
    {
        await using var scope = _fixture.Services.CreateAsyncScope();
        return await query(scope.ServiceProvider.GetRequiredService<IAccountQueries>());
    }

    [Fact]
    public async Task List_ShouldReturnAccountsWithCurrentBalance()
    {
        var accountId = await _fixture.CreateAccountAsync(100m);
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
        var accountId = await _fixture.CreateAccountAsync(100m);
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
        var accountId = await _fixture.CreateAccountAsync(0m);
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
        var accountId = await _fixture.CreateAccountAsync(50m);

        var statement = await QueryAsync(q => q.GetStatementAsync(accountId, 5, 10, CancellationToken.None));

        statement!.Items.Should().BeEmpty();
        statement.TotalItems.Should().Be(1);
    }
}
