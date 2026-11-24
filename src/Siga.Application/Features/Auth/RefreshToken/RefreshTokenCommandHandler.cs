using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Siga.Application.Abstractions.Data;
using Siga.Application.Abstractions.Messaging;
using Siga.Application.Abstractions.Security;
using Siga.Domain.Common;
using Siga.Domain.Users;

namespace Siga.Application.Features.Auth.RefreshToken;

internal sealed class RefreshTokenCommandHandler(
    IApplicationDbContext db,
    ITokenService tokenService,
    IRefreshTokenFactory refreshTokenFactory,
    IRequestContext requestContext,
    IOptions<AuthOptions> options,
    TimeProvider clock) : ICommandHandler<RefreshTokenCommand, AuthTokensResponse>
{
    public async Task<Result<AuthTokensResponse>> Handle(RefreshTokenCommand command, CancellationToken ct)
    {
        var opts = options.Value;
        var now = clock.GetUtcNow();
        var hash = refreshTokenFactory.ComputeHash(command.RefreshToken);

        var current = await db.RefreshTokens.SingleOrDefaultAsync(t => t.TokenHash == hash, ct);
        if (current is null)
            return await FailAsync(null, "Token inexistente", ct);

        // Reuso de un token ya rotado/revocado => posible robo: revocar toda la familia
        if (current.IsRevoked)
        {
            // AJUSTE: en lugar de ExecuteUpdateAsync se revocan las entidades rastreadas. Mismo efecto,
            // queda en la misma transacción que la bitácora y funciona con el proveedor InMemory de los tests.
            var family = await db.RefreshTokens
                .Where(t => t.FamilyId == current.FamilyId && t.RevokedAtUtc == null)
                .ToListAsync(ct);
            foreach (var token in family)
                token.Revoke(now, "ReuseDetected");

            Log(current.UserName, AccessEvents.RefreshReuseDetected, false, current.DeviceId, now);
            await db.SaveChangesAsync(ct);
            return AuthErrors.InvalidRefreshToken;
        }

        if (current.IsExpired(now))
            return await FailAsync(current.UserName, "Token expirado", ct);

        // El usuario debe seguir activo y habilitado
        var systemUser = await db.SystemUsers.AsNoTracking().SingleOrDefaultAsync(u => u.UserName == current.UserName, ct);
        var appUser = await db.AppUsers.AsNoTracking().SingleOrDefaultAsync(a => a.UserName == current.UserName, ct);
        if (systemUser is null || appUser is null || !opts.IsActiveStatus(systemUser.Status)
            || !appUser.AppAccessEnabled || appUser.IsLockedOut(now))
        {
            current.Revoke(now, "UserNotAllowed");
            return await FailAsync(current.UserName, "Usuario no permitido", ct);
        }

        // Rotación
        var next = refreshTokenFactory.Create();
        var nextExpiresAt = now.AddDays(opts.RefreshTokenDays);
        current.Revoke(now, "Rotated", next.Hash);
        db.RefreshTokens.Add(Domain.Users.RefreshToken.Issue(
            current.UserName, next.Hash, current.FamilyId, current.DeviceId,
            requestContext.IpAddress, requestContext.UserAgent, now, nextExpiresAt));

        var access = tokenService.CreateAccessToken(systemUser.UserName, systemUser.Name, systemUser.UserType, current.FamilyId);
        Log(current.UserName, AccessEvents.TokenRefreshed, true, current.DeviceId, now);
        await db.SaveChangesAsync(ct);

        return new AuthTokensResponse(access.Token, access.ExpiresAt, next.Token, nextExpiresAt,
            new UserProfileResponse(systemUser.UserName, systemUser.Name, systemUser.UserType));
    }

    private async Task<Result<AuthTokensResponse>> FailAsync(string? userName, string detail, CancellationToken ct)
    {
        Log(userName, AccessEvents.RefreshFailed, false, null, clock.GetUtcNow(), detail);
        await db.SaveChangesAsync(ct);
        return AuthErrors.InvalidRefreshToken;
    }

    private void Log(string? userName, string @event, bool ok, string? deviceId, DateTimeOffset now, string? detail = null) =>
        db.AccessLogs.Add(AccessLog.Create(userName, @event, ok, now,
            requestContext.IpAddress, deviceId, requestContext.UserAgent, detail));
}
