namespace Siga.Domain.Users;

/// <summary>Credencial y estado de acceso a la app móvil (seguridad.UsuariosApp).</summary>
public sealed class AppUser
{
    private AppUser() { }

    public string UserName { get; private set; } = default!;
    public string? PasswordHash { get; private set; }
    public bool AppAccessEnabled { get; private set; }
    public int FailedAttempts { get; private set; }
    public DateTimeOffset? LockoutEndUtc { get; private set; }
    public DateTimeOffset? LastLoginUtc { get; private set; }
    public DateTimeOffset? PasswordChangedUtc { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public byte[] RowVersion { get; private set; } = [];

    public static AppUser Enroll(string userName, bool appAccessEnabled, DateTimeOffset now) => new()
    {
        UserName = userName,
        AppAccessEnabled = appAccessEnabled,
        CreatedAtUtc = now
    };

    public bool HasPassword => PasswordHash is not null;

    public bool IsLockedOut(DateTimeOffset now) => LockoutEndUtc is { } end && end > now;

    public void RegisterFailedAttempt(DateTimeOffset now, int maxAttempts, TimeSpan lockoutDuration)
    {
        FailedAttempts++;
        if (FailedAttempts < maxAttempts) return;

        LockoutEndUtc = now.Add(lockoutDuration);
        FailedAttempts = 0;
    }

    public void RegisterSuccessfulLogin(DateTimeOffset now)
    {
        FailedAttempts = 0;
        LockoutEndUtc = null;
        LastLoginUtc = now;
    }

    public void SetPasswordHash(string passwordHash, DateTimeOffset now)
    {
        PasswordHash = passwordHash;
        PasswordChangedUtc = now;
    }
}
