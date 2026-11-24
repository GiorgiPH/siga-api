using Siga.Domain.Users;

namespace Siga.Domain.UnitTests.Users;

public sealed class RefreshTokenTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 16, 12, 0, 0, TimeSpan.Zero);

    private static RefreshToken NewToken(DateTimeOffset expiresAt) =>
        RefreshToken.Issue("jperez", new string('A', 64), Guid.NewGuid(), "device", "127.0.0.1", "ua", Now, expiresAt);

    [Fact]
    public void Revoke_AlreadyRevoked_KeepsOriginalReason()
    {
        var token = NewToken(Now.AddDays(14));

        token.Revoke(Now, "Rotated", new string('B', 64));
        token.Revoke(Now.AddMinutes(5), "Logout");

        token.IsRevoked.ShouldBeTrue();
        token.RevocationReason.ShouldBe("Rotated");
        token.RevokedAtUtc.ShouldBe(Now);
        token.ReplacedByTokenHash.ShouldBe(new string('B', 64));
    }

    [Fact]
    public void IsActive_Expired_ReturnsFalse()
    {
        var token = NewToken(Now.AddDays(14));

        token.IsActive(Now).ShouldBeTrue();
        token.IsActive(Now.AddDays(14)).ShouldBeFalse();
        token.IsExpired(Now.AddDays(15)).ShouldBeTrue();
    }
}
