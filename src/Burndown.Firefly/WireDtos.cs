namespace Burndown.Firefly;

// Wire format of the Firefly III API v1 (snake_case JSON). Ids are strings on the wire; they are turned into typed ids
// at the client boundary. These types never leave this assembly.

internal sealed record ListResponse<T>(List<ResourceDto<T>>? Data, MetaDto? Meta);

internal sealed record SingleResponse<T>(ResourceDto<T>? Data);

internal sealed record ResourceDto<T>(string? Id, T? Attributes);

internal sealed record MetaDto(PaginationDto? Pagination);

internal sealed record PaginationDto(int CurrentPage, int TotalPages);

internal sealed record AboutResponse(AboutDto? Data);

internal sealed record AboutDto(string? Version);

internal sealed record AccountAttributesDto(string? Name, bool? Active);

internal sealed record BudgetAttributesDto(string? Name, bool? Active);

internal sealed record CategoryAttributesDto(string? Name);

internal sealed record TagAttributesDto(string? Tag);

internal sealed record TransactionGroupAttributesDto(List<TransactionSplitDto>? Transactions);

internal sealed record TransactionSplitDto(
    string? TransactionJournalId,
    string? Type,
    string? Date,
    string? Amount,
    string? Description,
    string? SourceId,
    string? SourceType,
    string? DestinationId,
    string? DestinationType,
    string? BudgetId,
    string? CategoryName,
    List<string>? Tags
);

internal sealed record AutocompleteTransactionDto(string? Name, string? Description);

internal sealed record ValidationErrorResponse(Dictionary<string, string[]>? Errors);

internal sealed record CreateTransactionRequest(
    bool ApplyRules,
    bool FireWebhooks,
    bool ErrorIfDuplicateHash,
    List<NewTransactionSplitDto> Transactions
);

internal sealed record NewTransactionSplitDto(
    string Type,
    string Date,
    string Amount,
    string Description,
    string SourceId,
    string DestinationId,
    string? CategoryName,
    string? BudgetId,
    List<string> Tags
);
