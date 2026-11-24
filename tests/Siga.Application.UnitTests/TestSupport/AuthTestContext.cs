using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Siga.Application.Abstractions.Security;
using Siga.Application.Features.Auth;
using Siga.Application.Features.Auth.Login;
using Siga.Application.Features.Auth.Logout;
using Siga.Application.Features.Auth.RefreshToken;

namespace Siga.Application.UnitTests.TestSupport;

/// <summary>Arma los handlers de autenticación con dobles de prueba deterministas.</summary>
internal sealed class AuthTestContext : IDisposable
{
    public const string HashPrefix = "HASH:";

    public AuthTestContext()
    {
        Db = TestDbContext.Create();
        Clock = new FakeTimeProvider(new DateTimeOffset(2026, 11, 16, 12, 0, 0, TimeSpan.Zero));
        Options = Microsoft.Extensions.Options.Options.Create(new AuthOptions
        {
            MaxFailedAttempts = 5,
            LockoutMinutes = 15,
            RefreshTokenDays = 14,
            AutoEnrollOnFirstLogin = true,
            ActiveStatuses = ["ACTIVO"]
        });

        PasswordHasher = Substitute.For<IPasswordHasher>();
        PasswordHasher.Hash(Arg.Any<string>()).Returns(ci => HashPrefix + ci.Arg<string>());
        PasswordHasher.Verify(Arg.Any<string>(), Arg.Any<string>()).Returns(ci =>
            ci.ArgAt<string>(0) == HashPrefix + ci.ArgAt<string>(1) ? PasswordCheck.Success : PasswordCheck.Failed);

        LegacyVerifier = Substitute.For<ILegacyPasswordVerifier>();
        LegacyVerifier.Verify(Arg.Any<string?>(), Arg.Any<string>()).Returns(ci =>
            !string.IsNullOrEmpty(ci.ArgAt<string?>(0)) && ci.ArgAt<string?>(0) == ci.ArgAt<string>(1));

        TokenService = Substitute.For<ITokenService>();
        TokenService.CreateAccessToken(Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<Guid>())
            .Returns(_ => new AccessToken("jwt-" + Guid.NewGuid().ToString("N"), Clock.GetUtcNow().AddMinutes(15)));

        RefreshTokenFactory = Substitute.For<IRefreshTokenFactory>();
        RefreshTokenFactory.ComputeHash(Arg.Any<string>()).Returns(ci => "H-" + ci.Arg<string>());
        RefreshTokenFactory.Create().Returns(_ =>
        {
            var token = Guid.NewGuid().ToString("N");
            return new GeneratedRefreshToken(token, "H-" + token);
        });

        RequestContext = Substitute.For<IRequestContext>();
        RequestContext.IpAddress.Returns("127.0.0.1");
        RequestContext.UserAgent.Returns("tests");

        CurrentUser = Substitute.For<ICurrentUser>();
        CurrentUser.IsAuthenticated.Returns(true);
        CurrentUser.UserName.Returns("jperez");
    }

    public TestDbContext Db { get; }
    public FakeTimeProvider Clock { get; }
    public IOptions<AuthOptions> Options { get; }
    public IPasswordHasher PasswordHasher { get; }
    public ILegacyPasswordVerifier LegacyVerifier { get; }
    public ITokenService TokenService { get; }
    public IRefreshTokenFactory RefreshTokenFactory { get; }
    public IRequestContext RequestContext { get; }
    public ICurrentUser CurrentUser { get; }

    public LoginCommandHandler LoginHandler() => new(
        Db, PasswordHasher, LegacyVerifier, TokenService, RefreshTokenFactory, RequestContext, Options, Clock);

    public RefreshTokenCommandHandler RefreshHandler() => new(
        Db, TokenService, RefreshTokenFactory, RequestContext, Options, Clock);

    public LogoutCommandHandler LogoutHandler() => new(
        Db, RefreshTokenFactory, CurrentUser, RequestContext, Clock);

    public void Dispose() => Db.Dispose();
}
