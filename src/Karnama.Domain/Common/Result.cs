namespace Karnama.Domain.Common;

public enum ErrorKind
{
    Validation = 1,
    NotFound = 2,
    Conflict = 3,
    Forbidden = 4,
    Unauthorized = 5,
    TooManyRequests = 6,
}

/// <summary>A business error. <see cref="Code"/> is a stable translation key; <see cref="Message"/> is the Persian default.</summary>
public sealed record Error(string Code, string Message, ErrorKind Kind = ErrorKind.Validation)
{
    public static Error NotFound(string code = "errors.notFound", string message = "موردی پیدا نشد.") => new(code, message, ErrorKind.NotFound);
    public static Error Conflict(string code, string message) => new(code, message, ErrorKind.Conflict);
    public static Error Forbidden(string code = "errors.forbidden", string message = "به این بخش دسترسی ندارید.") => new(code, message, ErrorKind.Forbidden);
    public static Error Unauthorized(string code, string message) => new(code, message, ErrorKind.Unauthorized);
    public static Error Validation(string code, string message) => new(code, message, ErrorKind.Validation);
    public static Error TooMany(string code, string message) => new(code, message, ErrorKind.TooManyRequests);
}

public class Result
{
    protected Result(Error? error) => Error = error;

    public Error? Error { get; }
    public bool IsSuccess => Error is null;
    public bool IsFailure => Error is not null;

    public static Result Success() => new(null);
    public static Result Failure(Error error) => new(error);
    public static Result<T> Success<T>(T value) => Result<T>.Ok(value);

    public static implicit operator Result(Error error) => Failure(error);
}

public sealed class Result<T> : Result
{
    private readonly T? _value;

    private Result(T? value, Error? error) : base(error) => _value = value;

    public T Value => IsSuccess ? _value! : throw new InvalidOperationException("Cannot read the value of a failed result.");

    public static Result<T> Ok(T value) => new(value, null);
    public static new Result<T> Failure(Error error) => new(default, error);

    public static implicit operator Result<T>(T value) => Ok(value);
    public static implicit operator Result<T>(Error error) => Failure(error);
}
