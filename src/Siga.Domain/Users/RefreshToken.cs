namespace Siga.Domain.Users;

public sealed class RefreshToken
{
    private RefreshToken() { }

    public long Id { get; private set; }
    public string UserName { get; private set; } = default!;
    public string TokenHash { get; private set; } = default!;
    public Guid FamilyId { get; private set; }
    public string? DeviceId { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset ExpiresAtUtc { get; private set; }
    public DateTimeOffset? RevokedAtUtc { get; private set; }
    public string? RevocationReason { get; private set; }
    public string? ReplacedByTokenHash { get; private set; }
    public string? CreatedByIp { get; private set; }
    public string? UserAgent { get; private set; }

    public static RefreshToken Issue(
        string userName, string tokenHash, Guid familyId, string? deviceId,
        string? ip, string? userAgent, DateTimeOffset now, DateTimeOffset expiresAt) => new()
    {
        UserName = userName,
        TokenHash = tokenHash,
        FamilyId = familyId,
        DeviceId = deviceId,
        CreatedByIp = ip,
        UserAgent = userAgent,
        CreatedAtUtc = now,
        ExpiresAtUtc = expiresAt
    };

    public bool IsRevoked => RevokedAtUtc is not null;
    public bool IsExpired(DateTimeOffset now) => ExpiresAtUtc <= now;
    public bool IsActive(DateTimeOffset now) => !IsRevoked && !IsExpired(now);

    public void Revoke(DateTimeOffset now, string reason, string? replacedByTokenHash = null)
    {
        if (IsRevoked) return;
        RevokedAtUtc = now;
        RevocationReason = reason;
        ReplacedByTokenHash = replacedByTokenHash;
    }
}
