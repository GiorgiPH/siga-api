using System.Text.Json.Serialization;
using Siga.Domain.Common;

namespace Siga.Api.Contracts;

/// <summary>Sobre estándar de todas las respuestas de la API (compartido con la app móvil).</summary>
public sealed record ApiResponse<T>
{
    public bool Success { get; init; }
    public string Code { get; init; } = "OK";
    public string? Message { get; init; }
    public T? Data { get; init; }
    public IReadOnlyList<FieldError> Errors { get; init; } = [];
    public string? TraceId { get; init; }
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>Solo en Development: detalle técnico de excepciones.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Detail { get; init; }
}

public static class ApiResponse
{
    public static ApiResponse<T> Ok<T>(T data, string? traceId, string code = "OK") =>
        new() { Success = true, Code = code, Data = data, TraceId = traceId };

    public static ApiResponse<object> Fail(Error error, string? traceId)
    {
        ArgumentNullException.ThrowIfNull(error);
        return new()
        {
            Success = false,
            Code = error.Code,
            Message = error.Message,
            Errors = error is ValidationError v ? v.Errors : [],
            TraceId = traceId
        };
    }

    public static ApiResponse<object> Fail(string code, string message, string? traceId,
        IReadOnlyList<FieldError>? errors = null, string? detail = null) => new()
    {
        Success = false, Code = code, Message = message, TraceId = traceId, Errors = errors ?? [], Detail = detail
    };
}
