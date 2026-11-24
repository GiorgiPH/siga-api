using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Siga.Infrastructure.Persistence;

/// <summary>
/// AJUSTE: usada solo por dotnet-ef (migrations add / script) para no requerir secretos ni levantar la API.
/// Para "database update" se pasa la cadena real con --connection.
/// </summary>
internal sealed class SigaDbContextDesignTimeFactory : IDesignTimeDbContextFactory<SigaDbContext>
{
    public SigaDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<SigaDbContext>()
            .UseSqlServer(
                "Server=localhost;Database=Administracion;Integrated Security=True;TrustServerCertificate=True",
                sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", "seguridad"))
            .Options;
        return new SigaDbContext(options);
    }
}
