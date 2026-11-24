using Siga.Domain.Users;

namespace Siga.Domain.UnitTests.Users;

public sealed class AppUserTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 16, 12, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan Lockout = TimeSpan.FromMinutes(15);

    [Fact]
    public void RegisterFailedAttempt_FifthAttempt_LocksAccount()
    {
        var user = AppUser.Enroll("jperez", true, Now);

        for (var i = 0; i < 4; i++)
            user.RegisterFailedAttempt(Now, maxAttempts: 5, Lockout);
        user.IsLockedOut(Now).ShouldBeFalse();
        user.FailedAttempts.ShouldBe(4);

        user.RegisterFailedAttempt(Now, maxAttempts: 5, Lockout);

        user.IsLockedOut(Now).ShouldBeTrue();
        user.LockoutEndUtc.ShouldBe(Now.Add(Lockout));
        user.FailedAttempts.ShouldBe(0);
    }

    [Fact]
    public void RegisterSuccessfulLogin_ResetsAttemptsAndLockout()
    {
        var user = AppUser.Enroll("jperez", true, Now);
        for (var i = 0; i < 5; i++)
            user.RegisterFailedAttempt(Now, 5, Lockout);
        user.RegisterFailedAttempt(Now, 5, Lockout);

        user.RegisterSuccessfulLogin(Now);

        user.FailedAttempts.ShouldBe(0);
        user.LockoutEndUtc.ShouldBeNull();
        user.LastLoginUtc.ShouldBe(Now);
        user.IsLockedOut(Now).ShouldBeFalse();
    }

    [Fact]
    public void IsLockedOut_AfterLockoutEnds_ReturnsFalse()
    {
        var user = AppUser.Enroll("jperez", true, Now);
        for (var i = 0; i < 5; i++)
            user.RegisterFailedAttempt(Now, 5, Lockout);

        user.IsLockedOut(Now.Add(Lockout).AddSeconds(-1)).ShouldBeTrue();
        user.IsLockedOut(Now.Add(Lockout)).ShouldBeFalse();
    }
}
