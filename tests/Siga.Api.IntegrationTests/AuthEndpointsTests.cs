using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Siga.Api.IntegrationTests.Infrastructure;

namespace Siga.Api.IntegrationTests;

[Collection(ApiTestGroup.Name)]
public sealed class AuthEndpointsTests(SqlServerFixture db) : IDisposable
{
    private readonly SigaApiFactory _factory = new(db.ConnectionString);

    public void Dispose() => _factory.Dispose();

    [Fact]
    public async Task Login_ValidCredentials_Returns200WithEnvelope()
    {
        var client = _factory.CreateClient();

        var response = await client.LoginAsync(SqlServerFixture.ActiveUser, SqlServerFixture.ActivePassword);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var json = await response.ReadJsonAsync();
        json.GetProperty("success").GetBoolean().ShouldBeTrue();
        json.GetProperty("code").GetString().ShouldBe("OK");
        json.GetProperty("errors").GetArrayLength().ShouldBe(0);
        json.GetProperty("traceId").GetString().ShouldNotBeNullOrEmpty();
        json.TryGetProperty("timestamp", out _).ShouldBeTrue();

        var data = json.GetProperty("data");
        data.GetProperty("tokenType").GetString().ShouldBe("Bearer");
        data.GetProperty("accessToken").GetString().ShouldNotBeNullOrEmpty();
        data.GetProperty("refreshToken").GetString()!.Length.ShouldBe(86);
        data.GetProperty("user").GetProperty("userName").GetString().ShouldBe(SqlServerFixture.ActiveUser);
    }

    [Fact]
    public async Task Login_WrongPassword_Returns401InvalidCredentials()
    {
        var client = _factory.CreateClient();

        var response = await client.LoginAsync(SqlServerFixture.ActiveUser, "incorrecta");

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        var json = await response.ReadJsonAsync();
        json.GetProperty("success").GetBoolean().ShouldBeFalse();
        json.GetProperty("code").GetString().ShouldBe("AUTH_INVALID_CREDENTIALS");
    }

    [Fact]
    public async Task Login_InactiveUser_Returns403UserInactive()
    {
        var client = _factory.CreateClient();

        var response = await client.LoginAsync(SqlServerFixture.InactiveUser, SqlServerFixture.InactivePassword);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await response.ReadJsonAsync()).GetProperty("code").GetString().ShouldBe("AUTH_USER_INACTIVE");
    }

    [Fact]
    public async Task Me_WithoutToken_Returns401WithEnvelope()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync(new Uri("/api/v1/auth/me", UriKind.Relative));

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        var json = await response.ReadJsonAsync();
        json.GetProperty("success").GetBoolean().ShouldBeFalse();
        json.GetProperty("code").GetString().ShouldBe("UNAUTHORIZED");
    }

    [Fact]
    public async Task Me_WithToken_Returns200()
    {
        var client = _factory.CreateClient();
        var login = await (await client.LoginAsync(SqlServerFixture.ActiveUser, SqlServerFixture.ActivePassword)).ReadJsonAsync();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", login.GetProperty("data").GetProperty("accessToken").GetString());

        var response = await client.GetAsync(new Uri("/api/v1/auth/me", UriKind.Relative));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var data = (await response.ReadJsonAsync()).GetProperty("data");
        data.GetProperty("userName").GetString().ShouldBe(SqlServerFixture.ActiveUser);
        data.GetProperty("name").GetString().ShouldBe("Juan Pérez");
    }

    [Fact]
    public async Task Refresh_SameTokenTwice_SecondReturns401AndFamilyIsRevoked()
    {
        var client = _factory.CreateClient();
        var login = await (await client.LoginAsync(SqlServerFixture.ActiveUser, SqlServerFixture.ActivePassword)).ReadJsonAsync();
        var original = login.GetProperty("data").GetProperty("refreshToken").GetString();

        var first = await client.PostAsJsonAsync("/api/v1/auth/refresh", new { refreshToken = original });
        first.StatusCode.ShouldBe(HttpStatusCode.OK);
        var rotated = (await first.ReadJsonAsync()).GetProperty("data").GetProperty("refreshToken").GetString();

        var reuse = await client.PostAsJsonAsync("/api/v1/auth/refresh", new { refreshToken = original });
        reuse.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await reuse.ReadJsonAsync()).GetProperty("code").GetString().ShouldBe("AUTH_INVALID_REFRESH_TOKEN");

        // La familia quedó revocada: el token rotado tampoco sirve
        var afterReuse = await client.PostAsJsonAsync("/api/v1/auth/refresh", new { refreshToken = rotated });
        afterReuse.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task HealthReady_Returns200()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync(new Uri("/health/ready", UriKind.Relative));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task OpenApiDocument_ContainsLoginEndpoint()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync(new Uri("/openapi/v1.json", UriKind.Relative));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var json = await response.ReadJsonAsync();
        json.GetProperty("paths").TryGetProperty("/api/v1/auth/login", out _).ShouldBeTrue();
        json.GetProperty("components").GetProperty("securitySchemes").TryGetProperty("Bearer", out _).ShouldBeTrue();
    }

    [Fact]
    public async Task Login_MalformedJson_Returns400WithEnvelope()
    {
        var client = _factory.CreateClient();
        using var content = new StringContent("{ esto no es json", System.Text.Encoding.UTF8, "application/json");

        var response = await client.PostAsync(new Uri("/api/v1/auth/login", UriKind.Relative), content);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await response.ReadJsonAsync()).GetProperty("code").GetString().ShouldBe("BAD_REQUEST");
    }
}

[Collection(ApiTestGroup.Name)]
public sealed class RateLimitTests(SqlServerFixture db) : IDisposable
{
    // Factory propia: el rate limiter vive por instancia de host y no debe afectar a otras pruebas
    private readonly SigaApiFactory _factory = new(db.ConnectionString);

    public void Dispose() => _factory.Dispose();

    [Fact]
    public async Task Login_ElevenRequestsFromSameIp_Returns429()
    {
        var client = _factory.CreateClient();
        HttpResponseMessage? last = null;

        for (var i = 0; i < 11; i++)
            last = await client.LoginAsync("noexiste", "x"); // usuario inexistente: no provoca bloqueos de cuenta

        last!.StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);
        var json = await last.ReadJsonAsync();
        json.GetProperty("code").GetString().ShouldBe("RATE_LIMITED");
        json.GetProperty("success").ValueKind.ShouldBe(JsonValueKind.False);
    }
}
