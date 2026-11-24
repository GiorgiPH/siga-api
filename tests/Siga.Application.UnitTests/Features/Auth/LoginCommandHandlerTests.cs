using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Siga.Application.Behaviors;
using Siga.Application.Features.Auth;
using Siga.Application.Features.Auth.Login;
using Siga.Application.UnitTests.TestSupport;
using Siga.Domain.Common;
using Siga.Domain.Users;
using Siga.Application.Abstractions.Messaging;

namespace Siga.Application.UnitTests.Features.Auth;

public sealed class LoginCommandHandlerTests : IDisposable
{
    private readonly AuthTestContext _ctx = new();

    public void Dispose() => _ctx.Dispose();

    [Fact]
    public async Task Login_UnknownUser_ReturnsInvalidCredentials_AndCallsDummyVerify()
    {
        var result = await _ctx.LoginHandler().Handle(new LoginCommand("noexiste", "x", null), CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe(AuthErrors.InvalidCredentials);
        _ctx.PasswordHasher.Received(1).VerifyDummy("x");
        (await _ctx.Db.AccessLogs.SingleAsync()).Event.ShouldBe(AccessEvents.LoginFailed);
    }

    [Fact]
    public async Task Login_FirstLoginCorrectLegacyPassword_MigratesHash_ReturnsTokens()
    {
        _ctx.Db.SeedSystemUser("jperez", "MiClave123");

        var result = await _ctx.LoginHandler().Handle(new LoginCommand("jperez", "MiClave123", "device-1"), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.AccessToken.ShouldNotBeNullOrEmpty();
        result.Value.RefreshToken.ShouldNotBeNullOrEmpty();
        result.Value.User.ShouldBe(new UserProfileResponse("jperez", "Juan Pérez", "VENDEDOR"));

        var appUser = await _ctx.Db.AppUsers.SingleAsync();
        appUser.PasswordHash.ShouldBe(AuthTestContext.HashPrefix + "MiClave123");
        appUser.LastLoginUtc.ShouldBe(_ctx.Clock.GetUtcNow());
        (await _ctx.Db.RefreshTokens.CountAsync()).ShouldBe(1);
        (await _ctx.Db.AccessLogs.Select(l => l.Event).ToListAsync())
            .ShouldBe([AccessEvents.PasswordMigrated, AccessEvents.LoginSucceeded], ignoreOrder: true);
    }

    [Fact]
    public async Task Login_FirstLoginWrongLegacyPassword_IncrementsAttempts()
    {
        _ctx.Db.SeedSystemUser("jperez", "MiClave123");

        var result = await _ctx.LoginHandler().Handle(new LoginCommand("jperez", "otra", null), CancellationToken.None);

        result.Error.ShouldBe(AuthErrors.InvalidCredentials);
        var appUser = await _ctx.Db.AppUsers.SingleAsync();
        appUser.FailedAttempts.ShouldBe(1);
        appUser.PasswordHash.ShouldBeNull();
    }

    [Fact]
    public async Task Login_WithHash_IgnoresLegacyPassword()
    {
        _ctx.Db.SeedSystemUser("jperez", "MiClave123");
        (await _ctx.LoginHandler().Handle(new LoginCommand("jperez", "MiClave123", null), CancellationToken.None))
            .IsSuccess.ShouldBeTrue();
        _ctx.LegacyVerifier.ClearReceivedCalls();

        var result = await _ctx.LoginHandler().Handle(new LoginCommand("jperez", "MiClave123", null), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        _ctx.LegacyVerifier.DidNotReceiveWithAnyArgs().Verify(default, default!);
        _ctx.PasswordHasher.Received().Verify(AuthTestContext.HashPrefix + "MiClave123", "MiClave123");
    }

    [Fact]
    public async Task Login_FiveFailures_ReturnsAccountLocked()
    {
        _ctx.Db.SeedSystemUser("jperez", "MiClave123");
        var handler = _ctx.LoginHandler();

        for (var i = 0; i < 5; i++)
            (await handler.Handle(new LoginCommand("jperez", "mala", null), CancellationToken.None))
                .Error.ShouldBe(AuthErrors.InvalidCredentials);

        // Incluso con la contraseña correcta, la cuenta sigue bloqueada
        var result = await handler.Handle(new LoginCommand("jperez", "MiClave123", null), CancellationToken.None);

        result.Error.Code.ShouldBe("AUTH_ACCOUNT_LOCKED");
        result.Error.Type.ShouldBe(ErrorType.Forbidden);
    }

    [Fact]
    public async Task Login_InactiveStatus_ReturnsUserInactive()
    {
        _ctx.Db.SeedSystemUser("jperez", "MiClave123", status: "BAJA");

        var result = await _ctx.LoginHandler().Handle(new LoginCommand("jperez", "MiClave123", null), CancellationToken.None);

        result.Error.ShouldBe(AuthErrors.UserInactive);
        (await _ctx.Db.RefreshTokens.AnyAsync()).ShouldBeFalse();
    }

    [Fact]
    public async Task Login_AppAccessDisabled_ReturnsAppAccessDisabled()
    {
        _ctx.Db.SeedSystemUser("jperez", "MiClave123");
        _ctx.Db.AppUsers.Add(AppUser.Enroll("jperez", appAccessEnabled: false, _ctx.Clock.GetUtcNow()));
        await _ctx.Db.SaveChangesAsync();
        _ctx.Db.ChangeTracker.Clear();

        var result = await _ctx.LoginHandler().Handle(new LoginCommand("jperez", "MiClave123", null), CancellationToken.None);

        result.Error.ShouldBe(AuthErrors.AppAccessDisabled);
    }

    [Fact]
    public async Task Login_EmptyUserName_ReturnsValidationError()
    {
        var inner = Substitute.For<ICommandHandler<LoginCommand, AuthTokensResponse>>();
        var decorator = new ValidationCommandHandlerDecorator<LoginCommand, AuthTokensResponse>(
            inner, [new LoginCommandValidator()]);

        var result = await decorator.Handle(new LoginCommand("", "x", null), CancellationToken.None);

        var error = result.Error.ShouldBeOfType<ValidationError>();
        error.Code.ShouldBe("VALIDATION_ERROR");
        error.Errors.ShouldContain(e => e.Field == "userName" && e.Code == "REQUIRED");
        await inner.DidNotReceiveWithAnyArgs().Handle(default!, default);
    }
}
