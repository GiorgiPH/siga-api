using System.Diagnostics;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Siga.Api.Contracts;

namespace Siga.Api.ErrorHandling;

internal sealed partial class GlobalExceptionHandler(
    ILogger<GlobalExceptionHandler> logger, IHostEnvironment env) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception, CancellationToken ct)
    {
        var (status, code, message) = exception switch
        {
            DbUpdateConcurrencyException => (StatusCodes.Status409Conflict, "CONFLICT",
                "El registro fue modificado por otra operación. Intenta de nuevo."),
            DbUpdateException => (StatusCodes.Status409Conflict, "CONFLICT",
                "No fue posible completar la operación. Intenta de nuevo."),
            BadHttpRequestException => (StatusCodes.Status400BadRequest, "BAD_REQUEST",
                "La petición no tiene un formato válido."),
            OperationCanceledException => (499, "REQUEST_CANCELLED", "La petición fue cancelada."),
            _ => (StatusCodes.Status500InternalServerError, "SERVER_ERROR",
                "Ocurrió un error inesperado. Contacta a soporte con el folio.")
        };

        if (status >= 500)
            LogUnhandled(logger, exception, context.Request.Method, context.Request.Path);
        else
            LogHandled(logger, exception, code, context.Request.Path);

        var traceId = Activity.Current?.Id ?? context.TraceIdentifier;
        context.Response.StatusCode = status;
        await context.Response.WriteAsJsonAsync(
            ApiResponse.Fail(code, message, traceId, detail: env.IsDevelopment() ? exception.ToString() : null), ct);
        return true;
    }

    // AJUSTE: LoggerMessage generado en compilación (regla CA1848)
    [LoggerMessage(Level = LogLevel.Error, Message = "Excepción no controlada en {Method} {Path}")]
    private static partial void LogUnhandled(ILogger logger, Exception exception, string method, string path);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Excepción controlada {Code} en {Path}")]
    private static partial void LogHandled(ILogger logger, Exception exception, string code, string path);
}
