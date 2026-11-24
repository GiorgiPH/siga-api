using Siga.Domain.Common;

namespace Siga.Application.Abstractions.Messaging;

#pragma warning disable CA1040 // AJUSTE: interfaces marcadoras intencionales (tipan el resultado del comando/query)
public interface ICommand<TResponse>;
public interface IQuery<TResponse>;
#pragma warning restore CA1040

public interface ICommandHandler<in TCommand, TResponse> where TCommand : ICommand<TResponse>
{
    Task<Result<TResponse>> Handle(TCommand command, CancellationToken ct);
}

public interface IQueryHandler<in TQuery, TResponse> where TQuery : IQuery<TResponse>
{
    Task<Result<TResponse>> Handle(TQuery query, CancellationToken ct);
}
