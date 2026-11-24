using Microsoft.EntityFrameworkCore;
using Siga.Application.Abstractions.Data;
using Siga.Application.Abstractions.Messaging;
using Siga.Application.Abstractions.Security;
using Siga.Domain.Common;

namespace Siga.Application.Features.Auth.GetCurrentUser;

internal sealed class GetCurrentUserQueryHandler(IApplicationDbContext db, ICurrentUser currentUser)
    : IQueryHandler<GetCurrentUserQuery, UserProfileResponse>
{
    public async Task<Result<UserProfileResponse>> Handle(GetCurrentUserQuery query, CancellationToken ct)
    {
        var userName = currentUser.UserName;
        var profile = await db.SystemUsers.AsNoTracking()
            .Where(u => u.UserName == userName)
            .Select(u => new UserProfileResponse(u.UserName, u.Name, u.UserType))
            .SingleOrDefaultAsync(ct);

        return profile is null
            ? Error.NotFound("USER_NOT_FOUND", "El usuario ya no existe.")
            : profile;
    }
}
