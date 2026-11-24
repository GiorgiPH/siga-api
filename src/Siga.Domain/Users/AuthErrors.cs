using Siga.Domain.Common;

namespace Siga.Domain.Users;

public static class AuthErrors
{
    public static readonly Error InvalidCredentials =
        Error.Unauthorized("AUTH_INVALID_CREDENTIALS", "Usuario o contraseña incorrectos.");

    public static readonly Error UserInactive =
        Error.Forbidden("AUTH_USER_INACTIVE", "El usuario está inactivo. Contacta al administrador.");

    public static readonly Error AppAccessDisabled =
        Error.Forbidden("AUTH_APP_ACCESS_DISABLED", "Tu usuario no tiene acceso a la aplicación móvil.");

    public static readonly Error InvalidRefreshToken =
        Error.Unauthorized("AUTH_INVALID_REFRESH_TOKEN", "La sesión expiró. Inicia sesión nuevamente.");

    public static Error AccountLocked(DateTimeOffset until, DateTimeOffset now)
    {
        var minutes = Math.Max(1, (int)Math.Ceiling((until - now).TotalMinutes));
        return Error.Forbidden("AUTH_ACCOUNT_LOCKED",
            $"Cuenta bloqueada temporalmente por intentos fallidos. Intenta de nuevo en {minutes} minuto(s).");
    }
}
