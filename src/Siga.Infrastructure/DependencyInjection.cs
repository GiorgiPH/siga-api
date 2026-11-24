using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Siga.Application.Abstractions.Data;
using Siga.Application.Abstractions.Security;
using Siga.Infrastructure.Persistence;
using Siga.Infrastructure.Security;

namespace Siga.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services)
    {
        // AJUSTE: lectura diferida de la cadena de conexión + validación al arrancar (ver DatabaseOptions)
        services.AddOptions<DatabaseOptions>()
            .Configure<IConfiguration>((o, config) =>
                o.ConnectionString = config.GetConnectionString(DatabaseOptions.ConnectionStringName))
            .Validate(o => !string.IsNullOrWhiteSpace(o.ConnectionString),
                "Falta ConnectionStrings:Administracion. Configúrala con user-secrets o variables de entorno (ver README).")
            .ValidateOnStart();

        services.AddDbContext<SigaDbContext>((sp, o) => o.UseSqlServer(
            sp.GetRequiredService<IOptions<DatabaseOptions>>().Value.ConnectionString, sql =>
            {
                sql.EnableRetryOnFailure(maxRetryCount: 3);
                sql.CommandTimeout(30);
                sql.MigrationsHistoryTable("__EFMigrationsHistory", "seguridad");
            }));
        services.AddScoped<IApplicationDbContext>(sp => sp.GetRequiredService<SigaDbContext>());

        services.AddOptions<JwtOptions>()
            .BindConfiguration(JwtOptions.SectionName)
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddSingleton<IPasswordHasher, PasswordHasherService>();
        services.AddSingleton<ILegacyPasswordVerifier, LegacyPasswordVerifier>();
        services.AddSingleton<IRefreshTokenFactory, RefreshTokenFactory>();
        services.AddSingleton<ITokenService, JwtTokenService>();

        return services;
    }
}
