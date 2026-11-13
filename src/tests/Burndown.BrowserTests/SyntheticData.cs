using Burndown.Firefly;

namespace Burndown.BrowserTests;

/// <summary>Synthetic data: "now" is 15 February 2024 in Brussels, so January supplies the budget.</summary>
internal static class SyntheticData
{
    public static readonly DateTimeOffset Now = DateTimeOffset.Parse("2024-02-15T10:00:00+01:00");

    public static IReadOnlyList<FireflyTransaction> Transactions { get; } =
    [
        Tx(TransactionType.Deposit, 1, 25, 3000m, "Salary", "Asset account"),
        Tx(TransactionType.Withdrawal, 1, 3, 100m, "Loan", "Cash account", "MonthlyInstallment"),
        Tx(TransactionType.Withdrawal, 2, 1, 42.5m, "Bakery"),
        Tx(TransactionType.Withdrawal, 2, 1, 18m, "Pharmacy"),
        Tx(TransactionType.Withdrawal, 2, 3, 120m, "Groceries"),
        Tx(TransactionType.Withdrawal, 2, 6, 260m, "Car service"),
        Tx(TransactionType.Withdrawal, 2, 9, 35.75m, "Cinema"),
        Tx(TransactionType.Withdrawal, 2, 12, 15m, "Lunch"),
        Tx(TransactionType.Withdrawal, 2, 15, 22.4m, "Train ticket"),
    ];

    private static FireflyTransaction Tx(
        TransactionType type,
        int month,
        int day,
        decimal amount,
        string description,
        string destinationType = "Cash account",
        params string[] tags
    ) =>
        new(
            TransactionId.From((day * 100) + month),
            (day * 100) + month,
            type,
            new DateTimeOffset(2024, month, day, 12, 0, 0, TimeSpan.FromHours(1)),
            amount,
            description,
            AccountId.From(1),
            "Asset account",
            AccountId.From(2),
            destinationType,
            null,
            null,
            tags
        );
}
