using System.Diagnostics.CodeAnalysis;
using System.Runtime.ExceptionServices;
using Kevlar.Internal;

namespace Kevlar;

/// <summary>
/// The result of an execution: either a value or a captured exception, never both.
/// Kevlar strategies pass outcomes between pipeline layers instead of throwing, so
/// exceptions only pay their cost once, at the pipeline boundary.
/// </summary>
/// <typeparam name="T">The result type of the execution.</typeparam>
public readonly struct Outcome<T>
{
    private readonly T? _result;
    private readonly Exception? _exception;

    private Outcome(T? result, Exception? exception)
    {
        _result = result;
        _exception = exception;
    }

    /// <summary>The captured exception, or <see langword="null"/> if the execution produced a result.</summary>
    public Exception? Exception => _exception is KevlarProxyException proxy
        ? proxy.OriginalException
        : _exception;

    /// <summary>
    /// The result value. Only meaningful when <see cref="Exception"/> is <see langword="null"/>.
    /// For flow-analysis-friendly access, use <see cref="TryGetResult(out T)"/>.
    /// </summary>
    public T? Result => _result;

    /// <summary><see langword="true"/> when the execution produced a result rather than an exception.</summary>
    public bool IsSuccess => _exception is null;

    /// <summary>Creates an outcome holding a result value.</summary>
    public static Outcome<T> FromResult(T result) => new(result, null);

    /// <summary>Creates an outcome holding a captured exception.</summary>
    public static Outcome<T> FromException(Exception exception)
    {
        Throw.IfNull(exception, nameof(exception));
        return new Outcome<T>(default, exception);
    }

    /// <summary>Tries to get the result without throwing the captured exception.</summary>
    /// <param name="result">
    /// The result value when the outcome is successful; otherwise, the default value of <typeparamref name="T"/>.
    /// </param>
    /// <returns><see langword="true"/> when the execution produced a result; otherwise, <see langword="false"/>.</returns>
    public bool TryGetResult([MaybeNullWhen(false)] out T result)
    {
        if (IsSuccess)
        {
            result = _result!;
            return true;
        }

        result = default;
        return false;
    }

    /// <summary>
    /// Returns the result, or rethrows the captured exception with its original stack trace preserved.
    /// </summary>
    public T GetResultOrRethrow()
    {
        if (Exception is { } exception)
        {
            ExceptionDispatchInfo.Capture(exception).Throw();
        }

        return _result!;
    }

    internal T GetResultOrRethrowInternal()
    {
        if (_exception is { } exception)
        {
            ExceptionDispatchInfo.Capture(exception).Throw();
        }

        return _result!;
    }

    /// <inheritdoc />
    public override string ToString() => Exception?.ToString() ?? _result?.ToString() ?? string.Empty;
}
