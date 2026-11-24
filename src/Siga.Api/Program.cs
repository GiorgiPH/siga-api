using System.Globalization;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Serilog;
using Siga.Api.Extensions;
using Siga.Api.Middleware;
using Siga.Api.OpenApi;
using Siga.Application;
using Siga.Infrastructure;

Log.Logger = new LoggerConfiguration().WriteTo.Console(formatProvider: CultureInfo.InvariantCulture).CreateBootstrapLogger();

try
{
    var builder = WebApplication.CreateBuilder(args);

    builder.WebHost.ConfigureKestrel(k => k.AddServerHeader = false);
    builder.Host.UseSerilog((ctx, sp, cfg) => cfg
        .ReadFrom.Configuration(ctx.Configuration)
        .ReadFrom.Services(sp)
        .Enrich.FromLogContext());

    builder.Services
        .AddApplication()
        .AddInfrastructure()
        .AddPresentation();

    var app = builder.Build();

    app.UseForwardedHeaders();
    app.UseExceptionHandler();
    app.UseSerilogRequestLogging();
    if (!app.Environment.IsDevelopment()) app.UseHsts();
    app.UseHttpsRedirection();
    app.UseMiddleware<SecurityHeadersMiddleware>();
    app.UseAuthentication();
    app.UseAuthorization();
    app.UseRateLimiter();

    app.UseApiDocumentation();
    app.MapControllers().RequireAuthorization();   // seguro por defecto
    app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false }).AllowAnonymous();
    app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = c => c.Tags.Contains("ready") }).AllowAnonymous();

    await app.RunAsync();
}
catch (Exception ex) when (ex is not HostAbortedException)
{
    Log.Fatal(ex, "La aplicación terminó inesperadamente");
    throw;
}
finally
{
    await Log.CloseAndFlushAsync();
}

#pragma warning disable CA1050 // AJUSTE: Program debe ser público y global para WebApplicationFactory
public partial class Program;
#pragma warning restore CA1050
