using Vogen;

namespace Burndown.Firefly;

/// <summary>The Firefly id of an account.</summary>
[ValueObject<int>]
public readonly partial struct AccountId
{
    private static Validation Validate(int value) =>
        value > 0 ? Validation.Ok : Validation.Invalid("An account id must be positive.");
}

/// <summary>The Firefly id of a budget.</summary>
[ValueObject<int>]
public readonly partial struct BudgetId
{
    private static Validation Validate(int value) =>
        value > 0 ? Validation.Ok : Validation.Invalid("A budget id must be positive.");
}

/// <summary>The Firefly id of a transaction (a transaction group, which holds one or more splits).</summary>
[ValueObject<int>]
public readonly partial struct TransactionId
{
    private static Validation Validate(int value) =>
        value > 0 ? Validation.Ok : Validation.Invalid("A transaction id must be positive.");
}
