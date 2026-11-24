using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Siga.Infrastructure.Persistence;
using Testcontainers.MsSql;

namespace Siga.Api.IntegrationTests.Infrastructure;

/// <summary>
/// SQL Server en contenedor (Testcontainers). Crea la BD Administracion, la tabla legada dbo.Usuarios
/// con su DDL original y usuarios de prueba, y luego aplica las migraciones (esquema seguridad).
/// </summary>
public sealed class SqlServerFixture : IAsyncLifetime
{
    public const string ActiveUser = "jperez";
    public const string ActivePassword = "MiClave123";
    public const string InactiveUser = "inactivo";
    public const string InactivePassword = "Clave1";
    public const string NoPasswordUser = "sinclave";

    private readonly MsSqlContainer _container = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-latest").Build();

    public string ConnectionString { get; private set; } = default!;

    public async Task InitializeAsync()
    {
        await _container.StartAsync();

        await using (var master = new SqlConnection(_container.GetConnectionString()))
        {
            await master.OpenAsync();
            await ExecuteAsync(master, "CREATE DATABASE [Administracion];");
        }

        ConnectionString = new SqlConnectionStringBuilder(_container.GetConnectionString())
        {
            InitialCatalog = "Administracion"
        }.ConnectionString;

        await using (var db = new SqlConnection(ConnectionString))
        {
            await db.OpenAsync();
            await ExecuteAsync(db, """
                CREATE TABLE [dbo].[Usuarios](
                    [Usuario]     nvarchar(20)  NOT NULL PRIMARY KEY,
                    [Contraseña]  nvarchar(20)  NULL,
                    [Nombre]      nvarchar(50)  NULL,
                    [TipoUsuario] varchar(20)   NULL,
                    [FechaAlta]   date          NULL,
                    [Estatus]     varchar(10)   NULL,
                    [Nota]        nvarchar(100) NULL,
                    [Foto]        image         NULL
                );
                """);
            await ExecuteAsync(db, $"""
                INSERT INTO dbo.Usuarios (Usuario, [Contraseña], Nombre, TipoUsuario, FechaAlta, Estatus) VALUES
                    (N'{ActiveUser}',     N'{ActivePassword}',   N'Juan Pérez',   'VENDEDOR', '2020-01-01', 'ACTIVO'),
                    (N'{InactiveUser}',   N'{InactivePassword}', N'Usuario Baja', 'VENDEDOR', '2020-01-01', 'BAJA'),
                    (N'{NoPasswordUser}', NULL,                  N'Sin Clave',    'VENDEDOR', '2020-01-01', 'ACTIVO');
                """);
        }

        var options = new DbContextOptionsBuilder<SigaDbContext>()
            .UseSqlServer(ConnectionString, sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", "seguridad"))
            .Options;
        await using var context = new SigaDbContext(options);
        await context.Database.MigrateAsync();
    }

    public Task DisposeAsync() => _container.DisposeAsync().AsTask();

    private static async Task ExecuteAsync(SqlConnection connection, string sql)
    {
#pragma warning disable CA2100 // SQL fijo de pruebas, sin entrada de usuario
        await using var command = new SqlCommand(sql, connection);
#pragma warning restore CA2100
        await command.ExecuteNonQueryAsync();
    }
}

[CollectionDefinition(Name)]
public sealed class ApiTestGroup : ICollectionFixture<SqlServerFixture>
{
    public const string Name = "Api";
}
