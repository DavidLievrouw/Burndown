namespace Burndown.Common;

/// <summary>An expected failure: a stable machine-readable code and a user-readable message.</summary>
/// <param name="Code">Stable identifier of the failure kind, for example <c>validation</c> or <c>firefly.unauthorized</c>.</param>
/// <param name="Message">Message that is safe to show to the user. It never contains secrets or response bodies.</param>
/// <param name="Fields">
/// Per-field validation messages, keyed by field name (for example <c>description</c> or <c>source_id</c>), or
/// <see langword="null"/> when the failure is not tied to specific fields.
/// </param>
public sealed record Error(string Code, string Message, IReadOnlyDictionary<string, string[]>? Fields = null);

/// <summary>The outcome of an operation that can fail in an expected way and returns no value.</summary>
public readonly struct Result
{
    private readonly Error? _error;

    private Result(Error? error)
    {
        _error = error;
    }

    /// <summary>Gets a value indicating whether the operation succeeded.</summary>
    public bool IsSuccess => _error is null;

    /// <summary>Gets the failure. Throws when the operation succeeded.</summary>
    /// <exception cref="InvalidOperationException">The result is a success.</exception>
    public Error Error => _error ?? throw new InvalidOperationException("A successful result has no error.");

    /// <summary>Creates a successful result.</summary>
    /// <returns>A successful result.</returns>
    public static Result Success() => new(null);

    /// <summary>Creates a failed result.</summary>
    /// <param name="error">The failure.</param>
    /// <returns>A failed result.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="error"/> is <see langword="null"/>.</exception>
    public static Result Failure(Error error) => new(error ?? throw new ArgumentNullException(nameof(error)));
}

/// <summary>The outcome of an operation that can fail in an expected way and returns a value on success.</summary>
/// <typeparam name="T">The type of the value on success.</typeparam>
public readonly struct Result<T>
{
    private readonly T? _value;
    private readonly Error? _error;

    private Result(T? value, Error? error)
    {
        _value = value;
        _error = error;
    }

    /// <summary>Gets a value indicating whether the operation succeeded.</summary>
    public bool IsSuccess => _error is null;

    /// <summary>Gets the value. Throws when the operation failed.</summary>
    /// <exception cref="InvalidOperationException">The result is a failure.</exception>
    public T Value => _error is null ? _value! : throw new InvalidOperationException("A failed result has no value.");

    /// <summary>Gets the failure. Throws when the operation succeeded.</summary>
    /// <exception cref="InvalidOperationException">The result is a success.</exception>
    public Error Error => _error ?? throw new InvalidOperationException("A successful result has no error.");

    /// <summary>Creates a successful result.</summary>
    /// <param name="value">The value.</param>
    /// <returns>A successful result.</returns>
    public static Result<T> Success(T value) => new(value, null);

    /// <summary>Creates a failed result.</summary>
    /// <param name="error">The failure.</param>
    /// <returns>A failed result.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="error"/> is <see langword="null"/>.</exception>
    public static Result<T> Failure(Error error) =>
        new(default, error ?? throw new ArgumentNullException(nameof(error)));
}
