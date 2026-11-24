using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Siga.Api.IntegrationTests.Infrastructure;

/// <summary>
/// Host de la API en memoria apuntando al SQL Server del contenedor.
/// Cada instancia tiene su propio rate limiter (TestServer no tiene IP remota: todo cae en la partición "unknown").
/// </summary>
public sealed class SigaApiFactory(string connectionString) : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:Administracion", connectionString);
        builder.UseSetting("Jwt:SigningKey", "clave-de-pruebas-de-integracion-0123456789-abcdef");
        builder.UseSetting("OpenApi:Enabled", "true");
    }
}

internal static class HttpTestExtensions
{
    public static async Task<JsonElement> ReadJsonAsync(this HttpResponseMessage response)
    {
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return doc.RootElement.Clone();
    }

    public static Task<HttpResponseMessage> LoginAsync(this HttpClient client, string userName, string password) =>
        client.PostAsJsonAsync("/api/v1/auth/login", new { userName, password, deviceId = "test-device" });
}
