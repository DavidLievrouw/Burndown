using System.Globalization;
using Burndown.Common;
using Burndown.Firefly;
using Microsoft.Extensions.Logging;

namespace Burndown.Entry;

/// <summary>
/// Validates a batch, sequences the timestamps and creates the transactions in Firefly one by one in grid order.
/// A row is never sent twice and never retried automatically.
/// </summary>
internal sealed partial class EntrySubmitService
{
    /// <summary>The largest batch that is accepted.</summary>
    public const int MaxBatchSize = 100;

    private readonly IFireflyClient _firefly;
    private readonly AppClock _clock;
    private readonly EntryRowValidator _validator;
    private readonly EntryUsageTracker _usage;
    private readonly ILogger<EntrySubmitService> _logger;

    /// <summary>Initializes a new instance of the <see cref="EntrySubmitService"/> class.</summary>
    /// <param name="firefly">The Firefly client.</param>
    /// <param name="clock">The clock in the configured zone.</param>
    /// <param name="validator">The row validator.</param>
    /// <param name="usage">The last-use tracker, updated for every created row.</param>
    /// <param name="logger">The logger. It only receives counts, never descriptions.</param>
    public EntrySubmitService(
        IFireflyClient firefly,
        AppClock clock,
        EntryRowValidator validator,
        EntryUsageTracker usage,
        ILogger<EntrySubmitService> logger
    )
    {
        _firefly = firefly;
        _clock = clock;
        _validator = validator;
        _usage = usage;
        _logger = logger;
    }

    /// <summary>Submits a batch.</summary>
    /// <param name="request">The batch.</param>
    /// <param name="cancellationToken">
    /// Cancels the batch, for example when the browser went away. Rows that were not sent yet are then not sent.
    /// </param>
    /// <returns>The per-row results, or a validation failure when the batch itself is not acceptable.</returns>
    public async Task<Result<SubmitResponse>> SubmitAsync(SubmitRequest? request, CancellationToken cancellationToken)
    {
        if (request?.Rows is not { Count: > 0 } rows)
        {
            return BatchFailure("Add at least one row.");
        }

        if (rows.Count > MaxBatchSize)
        {
            return BatchFailure($"A batch can have at most {MaxBatchSize} rows.");
        }

        var previous = new List<DateTime>();
        foreach (var text in request.Previous ?? [])
        {
            if (
                !DateTime.TryParseExact(
                    text,
                    EntryRowValidator.DateTimeFormat,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.None,
                    out var time
                )
            )
            {
                return BatchFailure($"A previous time must be formatted as {EntryRowValidator.DateTimeFormat}.");
            }

            previous.Add(time);
        }

        var results = new RowResult?[rows.Count];
        var pending = Prepare(rows, previous, results);

        BatchStop? stopped = null;
        foreach (var item in pending)
        {
            if (stopped is not null)
            {
                results[item.Index] = Result(item, RowStatuses.NotAttempted);
                continue;
            }

            var created = await _firefly.CreateTransactionAsync(item.Transaction, cancellationToken);
            if (created.IsSuccess)
            {
                _usage.Record(item.Row.ChosenAccounts, item.Row.Category, item.Row.Tag, item.Row.Budget?.Value);
                results[item.Index] = Result(item, RowStatuses.Created) with { Id = created.Value.Id.Value };
                continue;
            }

            var error = created.Error;
            if (error.Code == FireflyErrorCodes.Validation)
            {
                results[item.Index] = Result(item, RowStatuses.Rejected) with
                {
                    Code = error.Code,
                    Message = error.Message,
                    Errors = MapFields(item.Row.Type, error.Fields),
                };
                continue;
            }

            var status = error.Code is FireflyErrorCodes.Timeout or FireflyErrorCodes.InvalidResponse
                ? RowStatuses.Uncertain
                : RowStatuses.Failed;
            results[item.Index] = Result(item, status) with { Code = error.Code, Message = error.Message };
            stopped = new BatchStop(item.Index, error.Code, error.Message);
        }

        var final = results.Select(r => r!).ToList();
        var summary = Summarize(final);
        LogBatch(_logger, final.Count, summary.Created, summary.Invalid, summary.Rejected, summary.Uncertain);
        return Result<SubmitResponse>.Success(new SubmitResponse(final, summary, stopped));
    }

    private List<PendingRow> Prepare(IReadOnlyList<SubmitRow> rows, List<DateTime> previous, RowResult?[] results)
    {
        var today = _clock.Today;
        var sequencer = new TimestampSequencer(_clock.Zone, _clock.Now.DateTime);
        foreach (var time in previous)
        {
            sequencer.Seed(time);
        }

        var validated = new Result<ValidRow>?[rows.Count];
        var locals = new DateTime?[rows.Count];
        for (var index = 0; index < rows.Count; index++)
        {
            var result = rows[index] is { } row
                ? _validator.Validate(row, today)
                : Result<ValidRow>.Failure(
                    new Error(ErrorCodes.Validation, "The row is not valid.", Field("row", "The row is empty."))
                );
            validated[index] = result;
            if (result.IsSuccess)
            {
                locals[index] = result.Value.Local;
            }
            else
            {
                results[index] = Invalid(index, result.Error);
            }
        }

        var placements = sequencer.Place(locals);
        var pending = new List<PendingRow>(rows.Count);
        for (var index = 0; index < rows.Count; index++)
        {
            if (placements[index] is not { } placed)
            {
                continue;
            }

            if (!placed.IsSuccess)
            {
                results[index] = Invalid(index, placed.Error);
                continue;
            }

            var valid = validated[index]!.Value.Value;
            pending.Add(new PendingRow(index, valid, placed.Value, ToTransaction(valid, placed.Value)));
        }

        return pending;
    }

    private static NewTransaction ToTransaction(ValidRow row, SequencedTime time) =>
        new(
            row.Type switch
            {
                RowTypes.Expense => TransactionType.Withdrawal,
                RowTypes.Income => TransactionType.Deposit,
                _ => TransactionType.Transfer,
            },
            time.Timestamp,
            row.Amount,
            row.Description,
            row.Source,
            row.Destination,
            row.Category,
            row.Budget,
            row.Tag is null ? [] : [row.Tag]
        );

    private static RowResult Result(PendingRow item, string status) =>
        new(
            item.Index,
            status,
            null,
            item.Time.Local.ToString(EntryRowValidator.DateTimeFormat, CultureInfo.InvariantCulture),
            item.Time.Bumped,
            null,
            null,
            null
        );

    private static RowResult Invalid(int index, Error error) =>
        new(index, RowStatuses.Invalid, null, null, false, error.Code, error.Message, error.Fields);

    private static Dictionary<string, string[]> Field(string field, string message) =>
        new(StringComparer.Ordinal) { [field] = [message] };

    private static Result<SubmitResponse> BatchFailure(string message) =>
        Result<SubmitResponse>.Failure(new Error(ErrorCodes.Validation, message, Field("rows", message)));

    private static SubmitSummary Summarize(List<RowResult> results)
    {
        int Count(string status) => results.Count(r => r.Status == status);
        return new SubmitSummary(
            Count(RowStatuses.Created),
            Count(RowStatuses.Invalid),
            Count(RowStatuses.Rejected),
            Count(RowStatuses.Failed),
            Count(RowStatuses.Uncertain),
            Count(RowStatuses.NotAttempted)
        );
    }

    /// <summary>Maps the field names of Firefly's 422 errors to the field names of the submitted row.</summary>
    /// <param name="type">The row type, which decides what the source and destination fields are called.</param>
    /// <param name="fields">Firefly's field messages, or <see langword="null"/> when there are none.</param>
    private static Dictionary<string, string[]>? MapFields(string type, IReadOnlyDictionary<string, string[]>? fields)
    {
        if (fields is null)
        {
            return null;
        }

        var mapped = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var (key, messages) in fields)
        {
            var field = MapField(type, key);
            if (!mapped.TryGetValue(field, out var list))
            {
                list = [];
                mapped[field] = list;
            }

            list.AddRange(messages.Where(m => !list.Contains(m, StringComparer.Ordinal)));
        }

        return mapped.ToDictionary(pair => pair.Key, pair => pair.Value.ToArray(), StringComparer.Ordinal);
    }

    private static string MapField(string type, string key)
    {
        var name = key.Split('.')[0];
        return name switch
        {
            "source_id" or "source_name" => type == RowTypes.Transfer ? "fromAccountId" : "accountId",
            "destination_id" or "destination_name" => type == RowTypes.Transfer ? "toAccountId" : "accountId",
            "category_id" or "category_name" => "category",
            "tags" => "tag",
            "budget_id" or "budget_name" => "budgetId",
            "date" => "dateTime",
            "description" or "amount" => name,
            _ => "row",
        };
    }

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Entry batch of {Rows} rows finished: {Created} created, {Invalid} invalid, {Rejected} rejected by Firefly, {Uncertain} uncertain."
    )]
    private static partial void LogBatch(
        ILogger logger,
        int rows,
        int created,
        int invalid,
        int rejected,
        int uncertain
    );

    private sealed record PendingRow(int Index, ValidRow Row, SequencedTime Time, NewTransaction Transaction);
}
