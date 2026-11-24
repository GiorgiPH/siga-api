using Siga.Application.Abstractions.Messaging;
using Siga.Domain.Common;

namespace Siga.Application.Features.Auth.Logout;

public sealed record LogoutCommand(string RefreshToken) : ICommand<Success>;
