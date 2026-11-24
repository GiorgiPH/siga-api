using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Siga.Application.Abstractions.Data;
using Siga.Application.Abstractions.Messaging;
using Siga.Application.Abstractions.Security;
using Siga.Domain.Common;
using Siga.Domain.Users;

namespace Siga.Application.Features.Auth.Login;

internal sealed class LoginCommandHandler(
    IApplicationDbContext db,
    IPasswordHasher passwordHasher,
    ILegacyPasswordVerifier legacyPasswordVerifier,
    ITokenService tokenService,
    IRefreshTokenFactory refreshTokenFactory,
    IRequestContext requestContext,
    IOptions<AuthOptions> options,
    TimeProvider clock) : ICommandHandler<LoginCommand, AuthTokensResponse>
{
    public async Task<Result<AuthTokensResponse>> Handle(LoginCommand command, CancellationToken ct)
    {
        var opts = options.Value;
        var now = clock.GetUtcNow();
        var userName = command.UserName.Trim();

        // 1. Identidad desde la tabla legada (solo columnas mapeadas, sin Foto)
        var systemUser = await db.SystemUsers.AsNoTracking()
            .SingleOrDefaultAsync(u => u.UserName == userName, ct);

        if (systemUser is null)
        {
            passwordHasher.VerifyDummy(command.Password); // evita enumeración por tiempo de respuesta
            Log(userName, AccessEvents.LoginFailed, false, command.DeviceId, now, "Usuario inexistente");
            await db.SaveChangesAsync(ct);
            return AuthErrors.InvalidCredentials;
        }

        // 2. Credencial de la app (se crea en el primer intento para poder contar fallos)
        var appUser = await db.AppUsers.SingleOrDefaultAsync(a => a.UserName == systemUser.UserName, ct);
        if (appUser is null)
        {
            appUser = AppUser.Enroll(systemUser.UserName, opts.AutoEnrollOnFirstLogin, now);
            db.AppUsers.Add(appUser);
        }

        // 3. Bloqueo temporal
        if (appUser.IsLockedOut(now))
        {
            Log(systemUser.UserName, AccessEvents.LoginBlocked, false, command.DeviceId, now);
            await db.SaveChangesAsync(ct);
            return AuthErrors.AccountLocked(appUser.LockoutEndUtc!.Value, now);
        }

        // 4. Contraseña (hash o migración desde texto plano)
        if (!VerifyPassword(appUser, systemUser, command.Password, now, command.DeviceId))
        {
            appUser.RegisterFailedAttempt(now, opts.MaxFailedAttempts, TimeSpan.FromMinutes(opts.LockoutMinutes));
            Log(systemUser.UserName, AccessEvents.LoginFailed, false, command.DeviceId, now, "Contraseña incorrecta");
            await db.SaveChangesAsync(ct);
            return AuthErrors.InvalidCredentials;
        }

        // 5. Estado del usuario (se valida DESPUÉS de la contraseña para no revelar información)
        if (!opts.IsActiveStatus(systemUser.Status))
        {
            Log(systemUser.UserName, AccessEvents.LoginFailed, false, command.DeviceId, now, "Usuario inactivo");
            await db.SaveChangesAsync(ct);
            return AuthErrors.UserInactive;
        }

        if (!appUser.AppAccessEnabled)
        {
            Log(systemUser.UserName, AccessEvents.LoginFailed, false, command.DeviceId, now, "Acceso app deshabilitado");
            await db.SaveChangesAsync(ct);
            return AuthErrors.AppAccessDisabled;
        }

        // 6. Emisión de tokens
        appUser.RegisterSuccessfulLogin(now);

        var sessionId = Guid.NewGuid(); // = FamilyId de los refresh tokens de esta sesión
        var accessToken = tokenService.CreateAccessToken(systemUser.UserName, systemUser.Name, systemUser.UserType, sessionId);
        var refresh = refreshTokenFactory.Create();
        var refreshExpiresAt = now.AddDays(opts.RefreshTokenDays);

        // AJUSTE: nombre calificado porque "RefreshToken" también es el namespace de la feature hermana
        db.RefreshTokens.Add(Domain.Users.RefreshToken.Issue(
            systemUser.UserName, refresh.Hash, sessionId, command.DeviceId,
            requestContext.IpAddress, requestContext.UserAgent, now, refreshExpiresAt));

        Log(systemUser.UserName, AccessEvents.LoginSucceeded, true, command.DeviceId, now);
        await db.SaveChangesAsync(ct);

        return new AuthTokensResponse(
            accessToken.Token, accessToken.ExpiresAt, refresh.Token, refreshExpiresAt,
            new UserProfileResponse(systemUser.UserName, systemUser.Name, systemUser.UserType));
    }

    private bool VerifyPassword(AppUser appUser, SystemUser systemUser, string password, DateTimeOffset now, string? deviceId)
    {
        if (appUser.HasPassword)
        {
            var check = passwordHasher.Verify(appUser.PasswordHash!, password);
            if (check == PasswordCheck.SuccessRehashNeeded)
                appUser.SetPasswordHash(passwordHasher.Hash(password), now);
            return check != PasswordCheck.Failed;
        }

        // Primer acceso: valida contra dbo.Usuarios.Contraseña y migra a hash
        if (!legacyPasswordVerifier.Verify(systemUser.LegacyPassword, password))
            return false;

        appUser.SetPasswordHash(passwordHasher.Hash(password), now);
        Log(systemUser.UserName, AccessEvents.PasswordMigrated, true, deviceId, now);
        return true;
    }

    private void Log(string? userName, string @event, bool ok, string? deviceId, DateTimeOffset now, string? detail = null) =>
        db.AccessLogs.Add(AccessLog.Create(userName, @event, ok, now,
            requestContext.IpAddress, deviceId, requestContext.UserAgent, detail));
}
