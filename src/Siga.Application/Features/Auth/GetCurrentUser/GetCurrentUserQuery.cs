using Siga.Application.Abstractions.Messaging;

namespace Siga.Application.Features.Auth.GetCurrentUser;

public sealed record GetCurrentUserQuery : IQuery<UserProfileResponse>;
