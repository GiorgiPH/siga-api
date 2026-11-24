namespace Siga.Domain.Common;

public enum ErrorType { Failure, Validation, NotFound, Conflict, Unauthorized, Forbidden }

public record Error(string Code, string Message, ErrorType Type)
{
    public static readonly Error None = new(string.Empty, string.Empty, ErrorType.Failure);

    public static Error Failure(string code, string message) => new(code, message, ErrorType.Failure);
    public static Error NotFound(string code, string message) => new(code, message, ErrorType.NotFound);
    public static Error Conflict(string code, string message) => new(code, message, ErrorType.Conflict);
    public static Error Unauthorized(string code, string message) => new(code, message, ErrorType.Unauthorized);
    public static Error Forbidden(string code, string message) => new(code, message, ErrorType.Forbidden);
}

public sealed record FieldError(string Field, string Code, string Message);

public sealed record ValidationError(IReadOnlyList<FieldError> Errors)
    : Error("VALIDATION_ERROR", "La información enviada no es válida.", ErrorType.Validation);
