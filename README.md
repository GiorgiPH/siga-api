# SIGA · Web API (Fase 1: Autenticación)

Web API ASP.NET Core (.NET 10) para la app móvil de **SIGA** (Sistema Integral de Gestión Administrativa).
Clean Architecture, BD SQL Server `Administracion`, autenticación con los usuarios existentes de `dbo.Usuarios`.

Diseño completo en [GUIA_SCAFFOLD_WEBAPI_ERP_LOGIN.md](GUIA_SCAFFOLD_WEBAPI_ERP_LOGIN.md) y decisiones en [docs/adr/](docs/adr/).

## Estructura

```
src/
  Siga.Domain          Entidades, Result/Error, catálogo AuthErrors (sin dependencias)
  Siga.Application     Comandos/queries, handlers, validadores, decoradores (Scrutor)
  Siga.Infrastructure  EF Core (SigaDbContext, migraciones), JWT, PBKDF2, refresh tokens
  Siga.Api             Controllers, sobre ApiResponse<T>, JWT bearer, rate limit, OpenAPI/Scalar/Swagger
tests/
  Siga.Domain.UnitTests · Siga.Application.UnitTests · Siga.Api.IntegrationTests · Siga.ArchitectureTests
database/
  001_seguridad_permisos.sql          Logins siga_api / siga_migrator con mínimo privilegio
  002_marcar_migracion_aplicada.sql   Para BD donde el esquema seguridad ya se creó a mano
  InitialSecuritySchema.sql           Script idempotente generado por EF (referencia)
```

## Endpoints

| Método | Ruta | Auth |
|---|---|---|
| POST | `/api/v1/auth/login` | Anónimo (rate limit 10/min por IP) |
| POST | `/api/v1/auth/refresh` | Anónimo (rate limit) |
| POST | `/api/v1/auth/logout` | Bearer |
| GET | `/api/v1/auth/me` | Bearer |
| GET | `/health/live`, `/health/ready` | Anónimo |
| GET | `/openapi/v1.json`, `/scalar/v1`, `/swagger` | Solo si `OpenApi:Enabled = true` (Development) |

Todas las respuestas usan el sobre `ApiResponse<T>` (`success`, `code`, `message`, `data`, `errors`, `traceId`, `timestamp`).

## Puesta en marcha

### 1. Requisitos
- .NET SDK 10
- SQL Server con la BD `Administracion` y la tabla `dbo.Usuarios`
- Docker Desktop (solo para los tests de integración)

### 2. Base de datos

**a) Permisos** (una vez, con un administrador): editar contraseñas y ejecutar `database/001_seguridad_permisos.sql`.

**b) Esquema `seguridad`:**

- **Si ya creaste las tablas a mano** (caso actual):
  1. Compara tus tablas con `database/InitialSecuritySchema.sql` (nombres, tipos y longitudes deben coincidir; la consulta del paso 1 de `002_...sql` te ayuda).
  2. Ejecuta `database/002_marcar_migracion_aplicada.sql` para registrar `InitialSecuritySchema` como aplicada.
- **Si la BD no tiene el esquema**, aplícalo con la migración:
  ```bash
  dotnet tool restore
  dotnet ef database update --project src/Siga.Infrastructure --startup-project src/Siga.Infrastructure \
    --connection "Server=...;Database=Administracion;User Id=siga_migrator;Password=...;Encrypt=True;TrustServerCertificate=True"
  ```
  En QA/producción usa el script idempotente revisado (con respaldo previo). La API **no** ejecuta migraciones al arrancar.

**c) Valores de `Estatus`:** revisa los reales y ajusta `Auth:ActiveStatuses` en `appsettings.json` (por defecto `["ACTIVO"]`, sin distinguir mayúsculas/espacios):
```sql
SELECT Estatus, COUNT(*) FROM dbo.Usuarios GROUP BY Estatus;
SELECT TipoUsuario, COUNT(*) FROM dbo.Usuarios GROUP BY TipoUsuario;
```

**d) Comparación de contraseña legada:** confirma si el sistema de escritorio distingue mayúsculas o recorta espacios y, si hace falta, ajusta `src/Siga.Infrastructure/Security/LegacyPasswordVerifier.cs`.

### 3. Secretos de desarrollo (NUNCA en appsettings)

La API **no arranca** sin estos dos valores (validación al inicio):

```bash
cd src/Siga.Api
dotnet user-secrets set "ConnectionStrings:Administracion" "Server=localhost;Database=Administracion;User Id=siga_api;Password=***;Encrypt=True;TrustServerCertificate=True;Application Name=Siga.Api"
dotnet user-secrets set "Jwt:SigningKey" "<cadena aleatoria de 32+ caracteres>"
```

PowerShell para generar una llave: `[Convert]::ToBase64String((1..48 | % { Get-Random -Max 256 }) -as [byte[]])`

En servidores: variables de entorno `ConnectionStrings__Administracion` y `Jwt__SigningKey`.
`TrustServerCertificate=True` solo en desarrollo local.

### 4. Compilar, probar y ejecutar

```bash
dotnet build Siga.Backend.slnx      # 0 warnings (TreatWarningsAsErrors)
dotnet test Siga.Backend.slnx       # integración requiere Docker en ejecución
dotnet run --project src/Siga.Api --launch-profile https
```

Documentación: https://localhost:7027/scalar/v1 y https://localhost:7027/swagger

## Migraciones

```bash
dotnet tool restore   # dotnet-ef como herramienta local (dotnet-tools.json)

# Nueva migración
dotnet ef migrations add <Nombre> --project src/Siga.Infrastructure --startup-project src/Siga.Infrastructure --output-dir Persistence/Migrations

# Script idempotente para revisar antes de aplicar
dotnet ef migrations script --idempotent --project src/Siga.Infrastructure --startup-project src/Siga.Infrastructure --output database/<Nombre>.sql
```

Verifica siempre que el script **no** contenga `CREATE`/`ALTER` sobre `dbo.Usuarios`.

## Ajustes respecto a la guía

Marcados en el código con `// AJUSTE:`:

- Prefijo `Siga.*` en lugar de `Erp.*` (`SigaDbContext`, Issuer `siga-api`, Audience `siga-mobile`, logins `siga_api`/`siga_migrator`).
- **Shouldly** en lugar de FluentAssertions (licencia comercial en v8).
- Paquetes extra: `Microsoft.Extensions.Options.ConfigurationExtensions` (`BindConfiguration`) y `Microsoft.Extensions.TimeProvider.Testing` (`FakeTimeProvider`).
- Revocación de familia de refresh tokens sobre entidades rastreadas en lugar de `ExecuteUpdateAsync`: mismo efecto, queda en la misma transacción que la bitácora y es compatible con EF InMemory.
- Cadena de conexión y parámetros JWT se leen de forma diferida (`IOptions`) y se validan al arrancar, para que los tests de integración puedan sobrescribirlos.
- `SigaDbContextDesignTimeFactory`: `dotnet ef` no necesita secretos para generar migraciones.
- Logging con `[LoggerMessage]` (regla CA1848) y supresiones puntuales y justificadas en `.editorconfig`.
