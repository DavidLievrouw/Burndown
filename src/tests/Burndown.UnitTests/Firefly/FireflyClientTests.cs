using System.Net;
using System.Text.Json;
using Burndown.Common;
using Burndown.Firefly;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;

namespace Burndown.UnitTests.Firefly;

public class FireflyClientTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static FireflyClient CreateClient(
        StubHandler handler,
        int pageSize = 100,
        ILogger<FireflyClient>? logger = null
    ) =>
        new(
            new HttpClient(handler) { BaseAddress = new Uri("http://firefly.test/api/v1/") },
            logger ?? NullLogger<FireflyClient>.Instance,
            pageSize
        );

    private static string Page(string items, int page, int totalPages) =>
        $$$$"""
            {"data":[{{{{items}}}}],"meta":{"pagination":{"total":0,"count":0,"per_page":2,"current_page":{{{{page}}}},"total_pages":{{{{totalPages}}}}}}}
            """;

    private static string Account(int id, string name, string type = "asset") =>
        $$$"""{"type":"accounts","id":"{{{id}}}","attributes":{"name":"{{{name}}}","active":true,"type":"{{{type}}}"}}""";

    private static string Split(
        string journalId = "11",
        string type = "withdrawal",
        string date = "2026-11-02T10:09:00+01:00",
        string amount = "12.500000000000",
        string? budgetId = null,
        string tags = """["SharedDavid"]"""
    ) =>
        $$$"""
            {"transaction_journal_id":"{{{journalId}}}","type":"{{{type}}}","date":"{{{date}}}","amount":"{{{amount}}}","description":"Bakery","source_id":"3","source_name":"Checking","source_type":"Asset account","destination_id":"6","destination_name":"Cash account","destination_type":"Cash account","budget_id":{{{(
                budgetId is null ? "null" : $"\"{budgetId}\""
            )}}},"category_name":"Nodig","tags":{{{tags}}}}
            """;

    private static string Group(int id, params string[] splits) =>
        $$$"""{"type":"transactions","id":"{{{id}}}","attributes":{"transactions":[{{{string.Join(',', splits)}}}]}}""";

    [Fact]
    public async Task About_returns_the_version()
    {
        var handler = new StubHandler(_ =>
            StubHandler.Json(HttpStatusCode.OK, """{"data":{"version":"6.7.8","api_version":"2.1.0"}}""")
        );

        var result = await CreateClient(handler).GetAboutAsync(Ct);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Version.ShouldBe("6.7.8");
        handler.Requests.Single().PathAndQuery.ShouldBe("/api/v1/about");
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, FireflyErrorCodes.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden, FireflyErrorCodes.Forbidden)]
    [InlineData(HttpStatusCode.InternalServerError, FireflyErrorCodes.Unavailable)]
    [InlineData(HttpStatusCode.BadGateway, FireflyErrorCodes.Unavailable)]
    [InlineData(HttpStatusCode.ServiceUnavailable, FireflyErrorCodes.Unavailable)]
    [InlineData(HttpStatusCode.NotFound, FireflyErrorCodes.Unexpected)]
    [InlineData(HttpStatusCode.Found, FireflyErrorCodes.Unexpected)]
    public async Task Failure_statuses_map_to_stable_codes_without_leaking_the_response(
        HttpStatusCode status,
        string code
    )
    {
        var handler = new StubHandler(_ => StubHandler.Json(status, """{"message":"secret internal detail"}"""));

        var result = await CreateClient(handler).GetAboutAsync(Ct);

        result.IsSuccess.ShouldBeFalse();
        result.Error.Code.ShouldBe(code);
        result.Error.Message.ShouldNotContain("secret internal detail");
        result.Error.Fields.ShouldBeNull();
    }

    [Fact]
    public async Task A_timeout_maps_to_a_timeout_error()
    {
        var handler = new StubHandler((_, _) => throw new TaskCanceledException("timed out"));

        var result = await CreateClient(handler).GetAboutAsync(Ct);

        result.Error.Code.ShouldBe(FireflyErrorCodes.Timeout);
    }

    [Fact]
    public async Task Cancellation_by_the_caller_is_not_swallowed()
    {
        using var source = new CancellationTokenSource();
        var handler = new StubHandler(
            async (_, token) =>
            {
                await source.CancelAsync();
                token.ThrowIfCancellationRequested();
                return StubHandler.Json(HttpStatusCode.OK, "{}");
            }
        );

        await Should.ThrowAsync<OperationCanceledException>(() => CreateClient(handler).GetAboutAsync(source.Token));
    }

    [Fact]
    public async Task A_connection_failure_maps_to_unavailable()
    {
        var handler = new StubHandler((_, _) => throw new HttpRequestException("connection refused"));

        var result = await CreateClient(handler).GetAboutAsync(Ct);

        result.Error.Code.ShouldBe(FireflyErrorCodes.Unavailable);
    }

    [Theory]
    [InlineData("<html>not json</html>")]
    [InlineData("null")]
    [InlineData("""{"data":{}}""")]
    public async Task A_successful_response_that_cannot_be_interpreted_is_an_invalid_response(string body)
    {
        var handler = new StubHandler(_ => StubHandler.Json(HttpStatusCode.OK, body));

        var result = await CreateClient(handler).GetAboutAsync(Ct);

        result.Error.Code.ShouldBe(FireflyErrorCodes.InvalidResponse);
    }

    [Fact]
    public async Task Validation_errors_are_keyed_by_field_without_the_position_prefix()
    {
        const string body = """
            {"message":"The given data was invalid.","errors":{
              "transactions.0.source_id":["Source is invalid.","Source is invalid."],
              "transactions.0.source_name":["Source name is invalid."],
              "description":["The description is required."]}}
            """;
        var handler = new StubHandler(_ => StubHandler.Json(HttpStatusCode.UnprocessableEntity, body));

        var result = await CreateClient(handler).CreateTransactionAsync(NewExpense(), Ct);

        result.Error.Code.ShouldBe(FireflyErrorCodes.Validation);
        var fields = result.Error.Fields.ShouldNotBeNull();
        fields.Keys.Order().ShouldBe(["description", "source_id", "source_name"]);
        fields["source_id"].ShouldBe(["Source is invalid."]);
        fields["description"].ShouldBe(["The description is required."]);
    }

    [Fact]
    public async Task An_unreadable_validation_body_is_still_a_validation_error()
    {
        var handler = new StubHandler(_ => StubHandler.Json(HttpStatusCode.UnprocessableEntity, "<html>oops</html>"));

        var result = await CreateClient(handler).CreateTransactionAsync(NewExpense(), Ct);

        result.Error.Code.ShouldBe(FireflyErrorCodes.Validation);
        result.Error.Fields.ShouldBeNull();
    }

    [Fact]
    public async Task Lists_follow_every_page_with_an_explicit_limit()
    {
        var handler = new StubHandler(request =>
        {
            var page = request.RequestUri!.Query.Contains("page=2", StringComparison.Ordinal) ? 2 : 1;
            var items = page == 1 ? Account(1, "Checking") + "," + Account(2, "Savings") : Account(3, "Wallet");
            return StubHandler.Json(HttpStatusCode.OK, Page(items, page, 2));
        });

        var result = await CreateClient(handler, pageSize: 2).GetAccountsAsync(AccountKind.Asset, Ct);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Select(a => a.Name).ShouldBe(["Checking", "Savings", "Wallet"]);
        result.Value.Select(a => a.Id.Value).ShouldBe([1, 2, 3]);
        result.Value.ShouldAllBe(a => a.Kind == AccountKind.Asset && a.Active);
        handler
            .Requests.Select(r => r.PathAndQuery)
            .ShouldBe(["/api/v1/accounts?type=asset&limit=2&page=1", "/api/v1/accounts?type=asset&limit=2&page=2"]);
    }

    [Fact]
    public async Task An_empty_list_without_pagination_metadata_is_a_single_page()
    {
        var handler = new StubHandler(_ => StubHandler.Json(HttpStatusCode.OK, """{"data":[]}"""));

        var result = await CreateClient(handler).GetBudgetsAsync(Ct);

        result.Value.ShouldBeEmpty();
        handler.Requests.Count.ShouldBe(1);
    }

    [Fact]
    public async Task A_server_that_never_stops_paginating_is_cut_off()
    {
        var handler = new StubHandler(_ => StubHandler.Json(HttpStatusCode.OK, Page(string.Empty, 1, int.MaxValue)));

        var result = await CreateClient(handler).GetTagsAsync(Ct);

        result.Error.Code.ShouldBe(FireflyErrorCodes.InvalidResponse);
        handler.Requests.Count.ShouldBeLessThanOrEqualTo(1000);
    }

    [Fact]
    public async Task A_failing_later_page_fails_the_whole_list()
    {
        var handler = new StubHandler(request =>
            request.RequestUri!.Query.Contains("page=2", StringComparison.Ordinal)
                ? StubHandler.Json(HttpStatusCode.InternalServerError, "{}")
                : StubHandler.Json(HttpStatusCode.OK, Page(Account(1, "Checking"), 1, 2))
        );

        var result = await CreateClient(handler, pageSize: 1).GetAccountsAsync(AccountKind.Asset, Ct);

        result.Error.Code.ShouldBe(FireflyErrorCodes.Unavailable);
    }

    [Fact]
    public async Task Budgets_categories_and_tags_are_mapped()
    {
        var handler = new StubHandler(request =>
            request.RequestUri!.AbsolutePath switch
            {
                "/api/v1/budgets" => StubHandler.Json(
                    HttpStatusCode.OK,
                    Page("""{"id":"13","attributes":{"name":"David","active":false}}""", 1, 1)
                ),
                "/api/v1/categories" => StubHandler.Json(
                    HttpStatusCode.OK,
                    Page("""{"id":"1","attributes":{"name":"Leven"}}""", 1, 1)
                ),
                _ => StubHandler.Json(
                    HttpStatusCode.OK,
                    Page("""{"id":"1","attributes":{"tag":"SharedDavid"}}""", 1, 1)
                ),
            }
        );
        var client = CreateClient(handler);

        var budgets = await client.GetBudgetsAsync(Ct);
        var categories = await client.GetCategoriesAsync(Ct);
        var tags = await client.GetTagsAsync(Ct);

        budgets.Value.Single().ShouldBe(new FireflyBudget(BudgetId.From(13), "David", false));
        categories.Value.ShouldBe(["Leven"]);
        tags.Value.ShouldBe(["SharedDavid"]);
    }

    [Fact]
    public async Task An_item_without_a_usable_id_is_an_invalid_response()
    {
        var handler = new StubHandler(_ =>
            StubHandler.Json(
                HttpStatusCode.OK,
                Page("""{"id":"0","attributes":{"name":"Broken","active":true}}""", 1, 1)
            )
        );

        var result = await CreateClient(handler).GetAccountsAsync(AccountKind.Asset, Ct);

        result.Error.Code.ShouldBe(FireflyErrorCodes.InvalidResponse);
    }

    [Fact]
    public async Task Transactions_are_flattened_to_splits_and_mapped()
    {
        var body = Page(
            Group(5, Split(journalId: "11", budgetId: "13"), Split(journalId: "12", budgetId: "0", tags: "[]"))
                + ","
                + Group(6, Split(journalId: "13", type: "opening balance")),
            1,
            1
        );
        var handler = new StubHandler(_ => StubHandler.Json(HttpStatusCode.OK, body));

        var result = await CreateClient(handler)
            .GetTransactionsAsync(
                new DateOnly(2026, 11, 1),
                new DateOnly(2026, 11, 30),
                TransactionType.Withdrawal,
                Ct
            );

        result.IsSuccess.ShouldBeTrue();
        result.Value.Count.ShouldBe(2);
        var first = result.Value[0];
        first.GroupId.Value.ShouldBe(5);
        first.JournalId.ShouldBe(11L);
        first.Type.ShouldBe(TransactionType.Withdrawal);
        first.Date.ShouldBe(new DateTimeOffset(2026, 11, 2, 10, 9, 0, TimeSpan.FromHours(1)));
        first.Date.Offset.ShouldBe(TimeSpan.FromHours(1));
        first.Amount.ShouldBe(12.5m);
        first.Description.ShouldBe("Bakery");
        first.SourceId.Value.ShouldBe(3);
        first.DestinationId.Value.ShouldBe(6);
        first.DestinationType.ShouldBe("Cash account");
        first.BudgetId.ShouldBe(BudgetId.From(13));
        first.Category.ShouldBe("Nodig");
        first.Tags.ShouldBe(["SharedDavid"]);
        result.Value[1].BudgetId.ShouldBeNull();
        result.Value[1].Tags.ShouldBeEmpty();
        handler
            .Requests.Single()
            .PathAndQuery.ShouldBe(
                "/api/v1/transactions?start=2026-11-01&end=2026-11-30&type=withdrawal&limit=100&page=1"
            );
    }

    [Fact]
    public async Task Transactions_of_every_type_are_requested_when_no_type_is_given()
    {
        var handler = new StubHandler(_ => StubHandler.Json(HttpStatusCode.OK, Page(string.Empty, 1, 1)));

        await CreateClient(handler).GetTransactionsAsync(new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 31), null, Ct);

        handler.Requests.Single().PathAndQuery.ShouldContain("type=all");
    }

    [Theory]
    [InlineData("amount", "twelve")]
    [InlineData("date", "yesterday")]
    public async Task A_malformed_split_is_an_invalid_response(string field, string value)
    {
        var split = field == "amount" ? Split(amount: value) : Split(date: value);
        var handler = new StubHandler(_ => StubHandler.Json(HttpStatusCode.OK, Page(Group(5, split), 1, 1)));

        var result = await CreateClient(handler)
            .GetTransactionsAsync(new DateOnly(2026, 11, 1), new DateOnly(2026, 11, 30), null, Ct);

        result.Error.Code.ShouldBe(FireflyErrorCodes.InvalidResponse);
    }

    [Fact]
    public async Task Description_suggestions_escape_the_query_and_are_distinct_ignoring_case()
    {
        var handler = new StubHandler(_ =>
            StubHandler.Json(
                HttpStatusCode.OK,
                """
                [{"id":"1","name":"Bakery","description":"Bakery"},
                 {"id":"2","name":"bakery","description":"bakery"},
                 {"id":"3","name":"Café & Co","description":"Café & Co"}]
                """
            )
        );

        var result = await CreateClient(handler).SuggestDescriptionsAsync("café & b?", 5, Ct);

        result.Value.ShouldBe(["Bakery", "Café & Co"]);
        handler
            .Requests.Single()
            .PathAndQuery.ShouldBe("/api/v1/autocomplete/transactions?query=caf%C3%A9%20%26%20b%3F&limit=5");
    }

    [Fact]
    public async Task Create_sends_a_typed_json_body_with_rules_and_webhooks_enabled()
    {
        var handler = new StubHandler(_ =>
            StubHandler.Json(
                HttpStatusCode.OK,
                $$$$"""{"data":{"id":"42","attributes":{"transactions":[{{{{Split()}}}}]}}}"""
            )
        );
        var transaction = NewExpense() with
        {
            Description = "Quote \" and backslash \\ done",
            Amount = 12.5m,
            Category = "Nodig",
            BudgetId = BudgetId.From(13),
            Tags = ["SharedDavid"],
        };

        var result = await CreateClient(handler).CreateTransactionAsync(transaction, Ct);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Id.Value.ShouldBe(42);
        var request = handler.Requests.Single();
        request.Method.ShouldBe(HttpMethod.Post);
        request.PathAndQuery.ShouldBe("/api/v1/transactions");
        using var json = JsonDocument.Parse(request.Body!);
        var root = json.RootElement;
        root.GetProperty("apply_rules").GetBoolean().ShouldBeTrue();
        root.GetProperty("fire_webhooks").GetBoolean().ShouldBeTrue();
        root.GetProperty("error_if_duplicate_hash").GetBoolean().ShouldBeFalse();
        var split = root.GetProperty("transactions").EnumerateArray().Single();
        split.GetProperty("type").GetString().ShouldBe("withdrawal");
        split.GetProperty("date").GetString().ShouldBe("2026-11-02T10:09:00+01:00");
        split.GetProperty("amount").GetString().ShouldBe("12.5");
        split.GetProperty("description").GetString().ShouldBe("Quote \" and backslash \\ done");
        split.GetProperty("source_id").GetString().ShouldBe("3");
        split.GetProperty("destination_id").GetString().ShouldBe("6");
        split.GetProperty("category_name").GetString().ShouldBe("Nodig");
        split.GetProperty("budget_id").GetString().ShouldBe("13");
        split.GetProperty("tags").EnumerateArray().Select(t => t.GetString()).ShouldBe(["SharedDavid"]);
    }

    [Fact]
    public async Task Create_omits_an_absent_budget_and_category()
    {
        var handler = new StubHandler(_ =>
            StubHandler.Json(HttpStatusCode.OK, """{"data":{"id":"42","attributes":{"transactions":[]}}}""")
        );

        await CreateClient(handler).CreateTransactionAsync(NewExpense(), Ct);

        using var json = JsonDocument.Parse(handler.Requests.Single().Body!);
        var split = json.RootElement.GetProperty("transactions").EnumerateArray().Single();
        split.TryGetProperty("budget_id", out _).ShouldBeFalse();
        split.TryGetProperty("category_name", out _).ShouldBeFalse();
    }

    [Fact]
    public async Task Create_is_sent_once_even_when_it_fails()
    {
        var handler = new StubHandler(_ => StubHandler.Json(HttpStatusCode.InternalServerError, "{}"));

        var result = await CreateClient(handler).CreateTransactionAsync(NewExpense(), Ct);

        result.Error.Code.ShouldBe(FireflyErrorCodes.Unavailable);
        handler.Requests.Count.ShouldBe(1);
    }

    [Fact]
    public async Task Logs_never_contain_the_query_string_or_the_description()
    {
        var logger = new ListLogger();
        var handler = new StubHandler(_ => StubHandler.Json(HttpStatusCode.InternalServerError, "{}"));
        var client = CreateClient(handler, logger: logger);

        await client.SuggestDescriptionsAsync("very private purchase", 5, Ct);
        await client.CreateTransactionAsync(NewExpense() with { Description = "very private purchase" }, Ct);

        logger.Messages.ShouldNotBeEmpty();
        logger.Messages.ShouldAllBe(m => !m.Contains("private", StringComparison.OrdinalIgnoreCase));
    }

    private static NewTransaction NewExpense() =>
        new(
            TransactionType.Withdrawal,
            new DateTimeOffset(2026, 11, 2, 10, 9, 0, TimeSpan.FromHours(1)),
            10m,
            "Bakery",
            AccountId.From(3),
            AccountId.From(6),
            null,
            null,
            []
        );

    private sealed class ListLogger : ILogger<FireflyClient>
    {
        public List<string> Messages { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter
        ) => Messages.Add(formatter(state, exception));
    }
}
