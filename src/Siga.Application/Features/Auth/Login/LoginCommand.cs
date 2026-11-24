using Siga.Application.Abstractions.Messaging;

namespace Siga.Application.Features.Auth.Login;

public sealed record LoginCommand(string UserName, string Password, string? DeviceId)
    : ICommand<AuthTokensResponse>;
