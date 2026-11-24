namespace Siga.Domain.Users;

/// <summary>Eventos registrados en la bitácora de accesos.</summary>
public static class AccessEvents
{
    public const string LoginSucceeded = "LoginSucceeded";
    public const string LoginFailed = "LoginFailed";
    public const string LoginBlocked = "LoginBlocked";
    public const string PasswordMigrated = "PasswordMigrated";
    public const string TokenRefreshed = "TokenRefreshed";
    public const string RefreshFailed = "RefreshFailed";
    public const string RefreshReuseDetected = "RefreshReuseDetected";
    public const string Logout = "Logout";
}
