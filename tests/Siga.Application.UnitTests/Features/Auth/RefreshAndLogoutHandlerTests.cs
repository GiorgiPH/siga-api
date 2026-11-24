using Microsoft.EntityFrameworkCore;
using Siga.Application.Features.Auth;
using Siga.Application.Features.Auth.Login;
using Siga.Application.Features.Auth.Logout;
using Siga.Application.Features.Auth.RefreshToken;
using Siga.Application.UnitTests.TestSupport;
using Siga.Domain.Users;

namespace Siga.Application.UnitTests.Features.Auth;

public sealed class RefreshAndLogoutHandlerTests : IDisposable
{
    private readonly AuthTestContext _ctx = new();

    public void Dispose() => _ctx.Dispose();

    private async Task<AuthTokensResponse> LoginAsync()
    {
        _ctx.Db.SeedSystemUser("jperez", "MiClave123");
        var result = await _ctx.LoginHandler().Handle(new LoginCommand("jperez", "MiClave123", "device-1"), CancellationToken.None);
        result.IsSuccess.ShouldBeTrue();
        _ctx.Db.ChangeTracker.Clear();
        return result.Value;
    }

    [Fact]
    public async Task Refresh_ValidToken_RotatesAndRevokesPrevious()
    {
        var login = await LoginAsync();

        var result = await _ctx.RefreshHandler().Handle(new RefreshTokenCommand(login.RefreshToken), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.RefreshToken.ShouldNotBe(login.RefreshToken);

        var tokens = await _ctx.Db.RefreshTokens.AsNoTracking().OrderBy(t => t.Id).ToListAsync();
        tokens.Count.ShouldBe(2);
        tokens[0].RevocationReason.ShouldBe("Rotated");
        tokens[0].ReplacedByTokenHash.ShouldBe(tokens[1].TokenHash);
        tokens[1].IsActive(_ctx.Clock.GetUtcNow()).ShouldBeTrue();
        tokens[1].FamilyId.ShouldBe(tokens[0].FamilyId);
    }

    [Fact]
    public async Task Refresh_RevokedToken_RevokesWholeFamily()
    {
        var login = await LoginAsync();
        (await _ctx.RefreshHandler().Handle(new RefreshTokenCommand(login.RefreshToken), CancellationToken.None))
            .IsSuccess.ShouldBeTrue();
        _ctx.Db.ChangeTracker.Clear();

        // Reuso del token original (ya rotado)
        var result = await _ctx.RefreshHandler().Handle(new RefreshTokenCommand(login.RefreshToken), CancellationToken.None);

        result.Error.ShouldBe(AuthErrors.InvalidRefreshToken);
        var tokens = await _ctx.Db.RefreshTokens.AsNoTracking().OrderBy(t => t.Id).ToListAsync();
        tokens.ShouldAllBe(t => t.RevokedAtUtc != null);
        tokens[1].RevocationReason.ShouldBe("ReuseDetected");
        (await _ctx.Db.AccessLogs.AnyAsync(l => l.Event == AccessEvents.RefreshReuseDetected)).ShouldBeTrue();
    }

    [Fact]
    public async Task Refresh_ExpiredToken_ReturnsInvalidRefreshToken()
    {
        var login = await LoginAsync();
        _ctx.Clock.Advance(TimeSpan.FromDays(15));

        var result = await _ctx.RefreshHandler().Handle(new RefreshTokenCommand(login.RefreshToken), CancellationToken.None);

        result.Error.ShouldBe(AuthErrors.InvalidRefreshToken);
        (await _ctx.Db.RefreshTokens.CountAsync()).ShouldBe(1);
    }

    [Fact]
    public async Task Logout_RevokesFamily_IsIdempotent()
    {
        var login = await LoginAsync();
        var refreshed = await _ctx.RefreshHandler().Handle(new RefreshTokenCommand(login.RefreshToken), CancellationToken.None);
        _ctx.Db.ChangeTracker.Clear();

        var first = await _ctx.LogoutHandler().Handle(new LogoutCommand(refreshed.Value.RefreshToken), CancellationToken.None);
        _ctx.Db.ChangeTracker.Clear();
        var second = await _ctx.LogoutHandler().Handle(new LogoutCommand(refreshed.Value.RefreshToken), CancellationToken.None);

        first.IsSuccess.ShouldBeTrue();
        second.IsSuccess.ShouldBeTrue();
        var tokens = await _ctx.Db.RefreshTokens.AsNoTracking().ToListAsync();
        tokens.ShouldAllBe(t => t.RevokedAtUtc != null);
        tokens.ShouldContain(t => t.RevocationReason == "Logout");
    }
}
