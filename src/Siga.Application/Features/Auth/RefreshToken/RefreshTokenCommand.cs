using Siga.Application.Abstractions.Messaging;

namespace Siga.Application.Features.Auth.RefreshToken;

public sealed record RefreshTokenCommand(string RefreshToken) : ICommand<AuthTokensResponse>;
