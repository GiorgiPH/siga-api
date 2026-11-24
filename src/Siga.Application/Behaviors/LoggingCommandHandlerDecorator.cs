using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Siga.Application.Abstractions.Messaging;
using Siga.Domain.Common;

namespace Siga.Application.Behaviors;

internal sealed partial class LoggingCommandHandlerDecorator<TCommand, TResponse>(
    ICommandHandler<TCommand, TResponse> inner,
    ILogger<LoggingCommandHandlerDecorator<TCommand, TResponse>> logger) : ICommandHandler<TCommand, TResponse>
    where TCommand : ICommand<TResponse>
{
    public async Task<Result<TResponse>> Handle(TCommand command, CancellationToken ct)
    {
        var name = typeof(TCommand).Name;          // NUNCA registrar el contenido del comando (contiene contraseñas)
        var sw = Stopwatch.StartNew();
        var result = await inner.Handle(command, ct);
        sw.Stop();

        if (result.IsFailure)
            LogFailed(logger, name, result.Error.Code, sw.ElapsedMilliseconds);
        else
            LogCompleted(logger, name, sw.ElapsedMilliseconds);

        return result;
    }

    // AJUSTE: LoggerMessage generado en compilación (regla CA1848 de AnalysisLevel latest-recommended)
    [LoggerMessage(Level = LogLevel.Warning, Message = "{Command} falló con {ErrorCode} en {ElapsedMs} ms")]
    private static partial void LogFailed(ILogger logger, string command, string errorCode, long elapsedMs);

    [LoggerMessage(Level = LogLevel.Information, Message = "{Command} completado en {ElapsedMs} ms")]
    private static partial void LogCompleted(ILogger logger, string command, long elapsedMs);
}
