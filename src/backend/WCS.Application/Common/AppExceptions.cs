namespace WCS.Application.Common;

public enum ErrorKind { Validation, NotFound, Conflict, Rejected, Unauthorized, Forbidden }

/// <summary>Expected business/validation failure. Mapped to a structured API error.</summary>
public class AppException(ErrorKind kind, string code, string message) : Exception(message)
{
    public ErrorKind Kind { get; } = kind;
    public string Code { get; } = code;

    public static AppException Validation(string code, string message) => new(ErrorKind.Validation, code, message);
    public static AppException NotFound(string code, string message) => new(ErrorKind.NotFound, code, message);
    public static AppException Conflict(string code, string message) => new(ErrorKind.Conflict, code, message);
    public static AppException Rejected(string code, string message) => new(ErrorKind.Rejected, code, message);
    public static AppException Unauthorized(string code, string message) => new(ErrorKind.Unauthorized, code, message);
    public static AppException Forbidden(string code, string message) => new(ErrorKind.Forbidden, code, message);
}

/// <summary>
/// Raised by the persistence layer on optimistic-concurrency failures or unique-constraint violations.
/// <see cref="Abstractions.IUnitOfWork.ExecuteAsync{T}"/> retries the whole unit of work when this happens.
/// </summary>
public class DataConflictException(string message, Exception? inner = null) : Exception(message, inner);
