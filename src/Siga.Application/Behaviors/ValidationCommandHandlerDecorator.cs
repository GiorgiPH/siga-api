using System.Text.Json;
using FluentValidation;
using Siga.Application.Abstractions.Messaging;
using Siga.Domain.Common;

namespace Siga.Application.Behaviors;

internal sealed class ValidationCommandHandlerDecorator<TCommand, TResponse>(
    ICommandHandler<TCommand, TResponse> inner,
    IEnumerable<IValidator<TCommand>> validators) : ICommandHandler<TCommand, TResponse>
    where TCommand : ICommand<TResponse>
{
    public async Task<Result<TResponse>> Handle(TCommand command, CancellationToken ct)
    {
        var failures = new List<FieldError>();
        foreach (var validator in validators)
        {
            var result = await validator.ValidateAsync(command, ct);
            failures.AddRange(result.Errors.Select(f => new FieldError(
                JsonNamingPolicy.CamelCase.ConvertName(f.PropertyName), f.ErrorCode, f.ErrorMessage)));
        }

        return failures.Count == 0
            ? await inner.Handle(command, ct)
            : new ValidationError(failures);
    }
}
