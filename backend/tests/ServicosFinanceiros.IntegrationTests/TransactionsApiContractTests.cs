using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace ServicosFinanceiros.IntegrationTests;

/// <summary>
/// Contrato HTTP do POST /api/transactions, do qual o frontend depende: cada situação precisa
/// virar o status e o ProblemDetails certos, e nenhum payload malformado pode chegar ao banco.
/// </summary>
[Collection(PostgresCollection.Name)]
public class TransactionsApiContractTests(PostgresFixture postgres) : IAsyncLifetime
{
    private readonly PostgresFixture _postgres = postgres;
    private WebApplicationFactory<Program> _api = null!;
    private HttpClient _client = null!;

    public Task InitializeAsync()
    {
        // Sem Redis: o rate limiting fica desligado e não interfere no contrato.
        _api = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.UseSetting("ConnectionStrings:Postgres", _postgres.ConnectionString);
        });
        _client = _api.CreateClient();
        return Task.CompletedTask;
    }

    public async Task DisposeAsync() => await _api.DisposeAsync();

    private static JsonObject ValidEvent(Guid accountId, string type = "CREDIT", decimal amount = 10m) => new()
    {
        ["eventId"] = Guid.NewGuid(),
        ["accountId"] = accountId,
        ["type"] = type,
        ["amount"] = amount,
        ["occurredAt"] = "2026-01-30T10:15:00Z",
    };

    private Task<HttpResponseMessage> PostAsync(JsonNode body) =>
        _client.PostAsync("/api/transactions", new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json"));

    private static async Task<JsonElement> JsonOf(HttpResponseMessage response) =>
        await response.Content.ReadFromJsonAsync<JsonElement>();

    [Fact]
    public async Task ValidEvent_Returns201WithBalanceAndLocationOfTheCreatedTransaction()
    {
        var accountId = await _postgres.CreateAccountAsync(100m);
        var body = ValidEvent(accountId, amount: 25.5m);

        var response = await PostAsync(body);
        var json = await JsonOf(response);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        json.GetProperty("balanceAfter").GetDecimal().Should().Be(125.5m);
        json.GetProperty("type").GetString().Should().Be("CREDIT");

        response.Headers.Location.Should().NotBeNull();
        var created = await _client.GetAsync(response.Headers.Location);
        created.StatusCode.Should().Be(HttpStatusCode.OK);
        (await JsonOf(created)).GetProperty("eventId").GetGuid().Should().Be(body["eventId"]!.GetValue<Guid>());
    }

    [Fact]
    public async Task SameEventTwice_Returns409ProblemDetails()
    {
        var accountId = await _postgres.CreateAccountAsync(100m);
        var body = ValidEvent(accountId);

        (await PostAsync(body)).StatusCode.Should().Be(HttpStatusCode.Created);
        var second = await PostAsync(body);

        second.StatusCode.Should().Be(HttpStatusCode.Conflict);
        second.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
    }

    [Fact]
    public async Task DebitAboveBalance_Returns422()
    {
        var accountId = await _postgres.CreateAccountAsync(10m);

        var response = await PostAsync(ValidEvent(accountId, "DEBIT", 10.01m));

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await JsonOf(response)).GetProperty("title").GetString().Should().Be("Saldo insuficiente");
    }

    [Fact]
    public async Task UnknownAccount_Returns404()
    {
        var response = await PostAsync(ValidEvent(Guid.NewGuid()));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task OccurredAtWithTimeZoneOffset_IsAcceptedAndStoredAsTheSameInstantInUtc()
    {
        // ISO-8601 válido com fuso de Brasília: antes disso a API respondia 500 (o Npgsql só aceita offset zero).
        var accountId = await _postgres.CreateAccountAsync(0m);
        var body = ValidEvent(accountId);
        body["occurredAt"] = "2026-01-30T10:15:00-03:00";

        var response = await PostAsync(body);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        (await JsonOf(response)).GetProperty("occurredAt").GetDateTimeOffset()
            .Should().Be(new DateTimeOffset(2026, 1, 30, 13, 15, 0, TimeSpan.Zero));
    }

    [Theory]
    [InlineData("eventId")]
    [InlineData("accountId")]
    [InlineData("type")]
    [InlineData("amount")]
    [InlineData("occurredAt")]
    public async Task MissingField_Returns400ForThatField_WithoutTouchingTheAccount(string field)
    {
        var accountId = await _postgres.CreateAccountAsync(100m);
        var body = ValidEvent(accountId);
        body.Remove(field);

        var response = await PostAsync(body);
        var errors = (await JsonOf(response)).GetProperty("errors");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        errors.EnumerateObject().Select(e => e.Name)
            .Should().Contain(name => string.Equals(name, field, StringComparison.OrdinalIgnoreCase));
        (await _postgres.ReadAccountAsync(accountId)).Transactions.Should().HaveCount(1); // só o crédito inicial
    }

    [Theory]
    [InlineData(2)]          // número em vez de "CREDIT"/"DEBIT"
    [InlineData("SAQUE")]    // valor fora do enum
    public async Task InvalidType_Returns400InPortugueseWithoutExposingInternalTypeNames(object type)
    {
        var accountId = await _postgres.CreateAccountAsync(100m);
        var body = ValidEvent(accountId);
        body["type"] = JsonValue.Create(type);

        var response = await PostAsync(body);
        var raw = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        raw.Should().NotContain("ServicosFinanceiros");
        raw.Should().Contain("formato inválido");
        // O erro é do campo "type"; o corpo em si não está ausente.
        (await JsonOf(response)).GetProperty("errors").EnumerateObject().Select(e => e.Name)
            .Should().Equal("type");
    }

    [Fact]
    public async Task MissingBody_Returns400InPortuguese()
    {
        var response = await _client.PostAsync("/api/transactions", new StringContent("", Encoding.UTF8, "application/json"));
        var json = await JsonOf(response);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        json.GetProperty("title").GetString().Should().Be("Dados inválidos");
    }

    [Fact]
    public async Task CreditThatWouldOverflowTheMaximumBalance_Returns400InsteadOf500()
    {
        var accountId = await _postgres.CreateAccountAsync(9_999_999_999_999_999m);

        var response = await PostAsync(ValidEvent(accountId, amount: 1m));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await _postgres.ReadAccountAsync(accountId)).Balance.Should().Be(9_999_999_999_999_999m);
    }

    [Fact]
    public async Task StatementPageFarBeyondTheEnd_Returns200WithEmptyPage()
    {
        var accountId = await _postgres.CreateAccountAsync(10m);

        var response = await _client.GetAsync($"/api/accounts/{accountId}/transactions?page={int.MaxValue}&pageSize=100");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await JsonOf(response)).GetProperty("items").GetArrayLength().Should().Be(0);
    }
}
