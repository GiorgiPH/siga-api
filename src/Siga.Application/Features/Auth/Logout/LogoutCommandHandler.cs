using Microsoft.EntityFrameworkCore;
using Siga.Application.Abstractions.Data;
using Siga.Application.Abstractions.Messaging;
using Siga.Application.Abstractions.Security;
using Siga.Domain.Common;
using Siga.Domain.Users;

namespace Siga.Application.Features.Auth.Logout;

internal sealed class LogoutCommandHandler(
    IApplicationDbContext db,
    IRefreshTokenFactory factory,
    ICurrentUser currentUser,
    IRequestContext requestContext,
    TimeProvider clock) : ICommandHandler<LogoutCommand, Success>
{
    public async Task<Result<Success>> Handle(LogoutCommand command, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var userName = currentUser.UserName;
        var hash = factory.ComputeHash(command.RefreshToken);

        var token = await db.RefreshTokens.SingleOrDefaultAsync(
            t => t.TokenHash == hash && t.UserName == userName, ct);

        // Idempotente: si no existe o ya está revocado, se responde éxito igualmente
        if (token is not null)
        {
            // AJUSTE: revocación sobre entidades rastreadas en lugar de ExecuteUpdateAsync (ver RefreshTokenCommandHandler)
            var family = await db.RefreshTokens
                .Where(t => t.FamilyId == token.FamilyId && t.RevokedAtUtc == null)
                .ToListAsync(ct);
            foreach (var item in family)
                item.Revoke(now, "Logout");
        }

        db.AccessLogs.Add(AccessLog.Create(userName, AccessEvents.Logout, true, now,
            requestContext.IpAddress, token?.DeviceId, requestContext.UserAgent));
        await db.SaveChangesAsync(ct);
        return Success.Instance;
    }
}
