# Guía de Scaffold — Web API ERP (ASP.NET Core .NET 10) · Fase 1: Autenticación

> **Documento para entregar a una IA generadora de código.**
> Contiene todas las decisiones, la estructura, las convenciones y el código base necesarios para generar el "cascarón" de la Web API, funcional y compilable, **hasta el login** (login, refresh token, logout y perfil del usuario), conectado a la base de datos `Administracion`.

---

## 0. Instrucciones para la IA (prompt)

Copia este bloque como instrucción inicial junto con el resto del documento:

```text
Actúa como arquitecto y desarrollador senior de .NET. Genera una solución ASP.NET Core (.NET 10)
siguiendo EXACTAMENTE esta guía:

1. Crea todos los proyectos, carpetas y archivos listados en la sección 4. No agregues proyectos,
   capas ni librerías que no estén en la sección 3.
2. Usa el código de referencia de la sección 8 tal cual; completa lo que falte siguiendo las mismas
   convenciones (sección 5). Si algo de la guía no compila con la versión instalada de un paquete,
   ajusta lo mínimo necesario y deja un comentario `// AJUSTE:` explicando el cambio.
3. NO modifiques la estructura de la tabla existente dbo.Usuarios. EF Core debe excluirla de migraciones.
4. Todo endpoint requiere autenticación excepto los marcados con [AllowAnonymous] en esta guía.
5. Genera la migración inicial `InitialSecuritySchema` (solo esquema `seguridad`).
6. Genera los tests de la sección 12.
7. Al terminar, verifica: `dotnet build` sin warnings y `dotnet test` en verde.
8. Entrega un README.md con los pasos de la sección 13 para levantar el proyecto.
El alcance es SOLO autenticación (login, refresh, logout, me) + infraestructura base. No generes
módulos de negocio.
```

---

## 1. Contexto y alcance

| Concepto | Valor |
|---|---|
| Producto | Web API para la app móvil del ERP (.NET MAUI) |
| Base de datos | SQL Server, BD `Administracion` |
| Usuarios | Tabla existente `dbo.Usuarios` (usada por el sistema de escritorio) |
| Alcance fase 1 | Infraestructura base + `login`, `refresh`, `logout`, `me` |
| Documentación de endpoints | OpenAPI nativo de .NET 10 + **Scalar** (`/scalar/v1`) + **Swagger UI** (`/swagger`) |
| Contrato de respuesta | Sobre `ApiResponse<T>` compartido con la app móvil |

**Fuera de alcance (fases siguientes):** cambio/recuperación de contraseña, permisos granulares, módulos de negocio (clientes, pedidos, inventario).

---

## 2. Decisiones de arquitectura (ADR resumidos)

### ADR-001 — Usar los usuarios existentes de `dbo.Usuarios`, sin duplicarlos

**Tabla existente:**

```sql
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
```

**Decisión:** la identidad del usuario (quién es, nombre, tipo, si está activo) **se lee de `dbo.Usuarios`**. Es la única fuente de verdad. Si el sistema de escritorio da de baja o inactiva a un usuario, automáticamente pierde acceso a la app.

**Por qué no crear una tabla de usuarios aparte:** duplicar usuarios genera altas y bajas desincronizadas, doble administración y riesgo de que un empleado dado de baja siga entrando por la app.

### ADR-002 — Credenciales de la app en un esquema propio `seguridad`, con hash

**Hallazgo crítico:** `Contraseña nvarchar(20)` indica que las contraseñas están en **texto plano** (un hash seguro ocupa 60–100+ caracteres). La API **no debe depender de contraseñas en texto plano** más allá del primer acceso.

**Decisión — migración progresiva ("lazy migration"):**

1. Se crea el esquema `seguridad` con tablas propias de la API. **No se altera `dbo.Usuarios`.**
2. **Primer login en la app:** la API valida contra `dbo.Usuarios.Contraseña` (comparación en tiempo constante). Si es correcta, genera un **hash PBKDF2 (ASP.NET Core Identity)** y lo guarda en `seguridad.UsuariosApp.PasswordHash`.
3. **Logins siguientes:** se valida **solo contra el hash**. La columna en texto plano deja de usarse para ese usuario.
4. **Reinicio de contraseña:** un administrador pone `PasswordHash = NULL` en `seguridad.UsuariosApp`. En el siguiente login el usuario vuelve a entrar con su contraseña del sistema de escritorio, y se genera un hash nuevo.

**Consecuencia aceptada:** después del primer login, la contraseña de la app es independiente de la del sistema de escritorio. Si el usuario cambia la contraseña en el escritorio, la app conserva la anterior hasta que se reinicie (paso 4) o hasta que exista el endpoint de cambio de contraseña (fase 2).

**Recomendación fuerte (fuera de este proyecto):** migrar el sistema de escritorio a contraseñas con hash. Mientras tanto:

- El usuario SQL de la API tiene permiso `SELECT` **solo** sobre las columnas necesarias de `dbo.Usuarios` (sección 6.4).
- Nunca se registra en logs la contraseña ni se devuelve en ninguna respuesta.

### ADR-003 — Tablas nuevas del esquema `seguridad`

| Tabla | Propósito |
|---|---|
| `seguridad.UsuariosApp` | Credencial con hash, habilitación de acceso a la app, intentos fallidos, bloqueo, último acceso. Relación 1:1 con `dbo.Usuarios`. |
| `seguridad.RefreshTokens` | Refresh tokens **hasheados (SHA-256)**, con rotación y detección de reuso por familia (sesión/dispositivo). |
| `seguridad.BitacoraAccesos` | Auditoría de intentos de login, refresh y logout (exitosos y fallidos). |

Los nombres de objetos de BD van **en español** para ser consistentes con la BD existente. El código C# va **en inglés** y se mapea con `HasColumnName`.

### ADR-004 — Sin MediatR ni AutoMapper

MediatR y AutoMapper pasaron a licencia comercial en 2025. Se usan **interfaces propias** `ICommandHandler`/`IQueryHandler`, **decoradores con Scrutor** (validación y logging) y **mapeo manual**. El resultado es igual de limpio, sin costo de licencia y sin "magia" difícil de depurar.

### ADR-005 — JWT corto + refresh token rotativo

- Access token JWT HS256, **15 minutos**.
- Refresh token opaco aleatorio de 64 bytes, **14 días**, guardado como hash SHA-256 y **rotado en cada uso**.
- Si se presenta un refresh token ya revocado (posible robo), **se revoca toda la familia** (todas las sesiones derivadas de ese login).

---

## 3. Stack y paquetes NuGet

> Versiones: usar la última estable compatible con .NET 10 al momento de generar. Centralizar en `Directory.Packages.props`.

| Proyecto | Paquete | Uso |
|---|---|---|
| Application | `Microsoft.EntityFrameworkCore` | `DbSet` en `IApplicationDbContext` |
| Application | `FluentValidation`, `FluentValidation.DependencyInjectionExtensions` | Validación de comandos |
| Application | `Scrutor` | Registro por escaneo y decoradores |
| Application | `Microsoft.Extensions.Options.DataAnnotations` | Validación de opciones |
| Infrastructure | `Microsoft.EntityFrameworkCore.SqlServer` | ORM |
| Infrastructure | `Microsoft.EntityFrameworkCore.Design` | Migraciones (PrivateAssets=all) |
| Infrastructure | `Microsoft.Extensions.Identity.Core` | `PasswordHasher<T>` (PBKDF2) |
| Infrastructure | `Microsoft.IdentityModel.JsonWebTokens` | Generar JWT |
| Api | `Microsoft.AspNetCore.Authentication.JwtBearer` | Validar JWT |
| Api | `Microsoft.AspNetCore.OpenApi` | Documento OpenAPI nativo |
| Api | `Scalar.AspNetCore` | UI de documentación moderna |
| Api | `Swashbuckle.AspNetCore.SwaggerUI` | UI Swagger clásica (solo la UI, lee el OpenAPI nativo) |
| Api | `Serilog.AspNetCore` | Logging estructurado |
| Api | `Microsoft.Extensions.Diagnostics.HealthChecks.EntityFrameworkCore` | Health check de BD |
| Tests | `xunit`, `xunit.runner.visualstudio`, `Microsoft.NET.Test.Sdk`, `NSubstitute`, `FluentAssertions` (o `Shouldly`), `Microsoft.AspNetCore.Mvc.Testing`, `Testcontainers.MsSql`, `NetArchTest.Rules`, `Microsoft.EntityFrameworkCore.InMemory` | Pruebas |

---

## 4. Estructura de la solución

```text
Erp.Backend/
├── Erp.Backend.slnx
├── Directory.Build.props
├── Directory.Packages.props
├── .editorconfig
├── .gitignore
├── README.md
├── docs/
│   └── adr/                                   # ADR-001..005 de la sección 2, un archivo cada uno
├── database/
│   └── 001_seguridad_permisos.sql             # Sección 6.4 (usuarios SQL y permisos)
├── src/
│   ├── Erp.Domain/
│   │   ├── Common/
│   │   │   ├── Error.cs                       # Error, ErrorType, FieldError, ValidationError
│   │   │   ├── Result.cs                      # Result, Result<T>, Success
│   │   └── Users/
│   │       ├── SystemUser.cs                  # Mapeo de dbo.Usuarios (solo lectura)
│   │       ├── AppUser.cs                     # seguridad.UsuariosApp
│   │       ├── RefreshToken.cs                # seguridad.RefreshTokens
│   │       ├── AccessLog.cs                   # seguridad.BitacoraAccesos
│   │       ├── AccessEvents.cs                # Constantes de eventos de bitácora
│   │       └── AuthErrors.cs                  # Catálogo de errores de autenticación
│   │
│   ├── Erp.Application/
│   │   ├── Abstractions/
│   │   │   ├── Data/IApplicationDbContext.cs
│   │   │   ├── Messaging/ICommand.cs           # ICommand<T>, IQuery<T>, ICommandHandler, IQueryHandler
│   │   │   └── Security/
│   │   │       ├── IPasswordHasher.cs          # + enum PasswordCheck
│   │   │       ├── ILegacyPasswordVerifier.cs
│   │   │       ├── ITokenService.cs            # + record AccessToken
│   │   │       ├── IRefreshTokenFactory.cs     # + record GeneratedRefreshToken
│   │   │       ├── ICurrentUser.cs
│   │   │       └── IRequestContext.cs
│   │   ├── Behaviors/
│   │   │   ├── ValidationCommandHandlerDecorator.cs
│   │   │   └── LoggingCommandHandlerDecorator.cs
│   │   ├── Features/Auth/
│   │   │   ├── AuthOptions.cs
│   │   │   ├── AuthTokensResponse.cs           # + UserProfileResponse
│   │   │   ├── Login/
│   │   │   │   ├── LoginCommand.cs
│   │   │   │   ├── LoginCommandHandler.cs
│   │   │   │   └── LoginCommandValidator.cs
│   │   │   ├── RefreshToken/
│   │   │   │   ├── RefreshTokenCommand.cs
│   │   │   │   ├── RefreshTokenCommandHandler.cs
│   │   │   │   └── RefreshTokenCommandValidator.cs
│   │   │   ├── Logout/
│   │   │   │   ├── LogoutCommand.cs
│   │   │   │   ├── LogoutCommandHandler.cs
│   │   │   │   └── LogoutCommandValidator.cs
│   │   │   └── GetCurrentUser/
│   │   │       ├── GetCurrentUserQuery.cs
│   │   │       └── GetCurrentUserQueryHandler.cs
│   │   └── DependencyInjection.cs              # AddApplication()
│   │
│   ├── Erp.Infrastructure/
│   │   ├── Persistence/
│   │   │   ├── ErpDbContext.cs
│   │   │   ├── Configurations/
│   │   │   │   ├── SystemUserConfiguration.cs
│   │   │   │   ├── AppUserConfiguration.cs
│   │   │   │   ├── RefreshTokenConfiguration.cs
│   │   │   │   └── AccessLogConfiguration.cs
│   │   │   └── Migrations/                     # Generada: InitialSecuritySchema
│   │   ├── Security/
│   │   │   ├── JwtOptions.cs
│   │   │   ├── JwtTokenService.cs
│   │   │   ├── PasswordHasherService.cs
│   │   │   ├── LegacyPasswordVerifier.cs
│   │   │   └── RefreshTokenFactory.cs
│   │   └── DependencyInjection.cs              # AddInfrastructure(config)
│   │
│   └── Erp.Api/
│       ├── Program.cs
│       ├── appsettings.json
│       ├── appsettings.Development.json
│       ├── Properties/launchSettings.json
│       ├── Contracts/
│       │   ├── ApiResponse.cs                  # ApiResponse<T> + fábrica ApiResponse
│       │   └── Auth/
│       │       ├── LoginRequest.cs
│       │       ├── RefreshTokenRequest.cs
│       │       └── LogoutRequest.cs
│       ├── Controllers/
│       │   ├── ApiControllerBase.cs
│       │   └── V1/AuthController.cs
│       ├── ErrorHandling/
│       │   ├── GlobalExceptionHandler.cs
│       │   └── AuthResponseWriter.cs           # 401/403 del middleware JWT con el sobre estándar
│       ├── OpenApi/
│       │   ├── BearerSecuritySchemeTransformer.cs
│       │   └── OpenApiExtensions.cs
│       ├── Middleware/
│       │   └── SecurityHeadersMiddleware.cs
│       ├── Services/
│       │   ├── CurrentUser.cs
│       │   └── HttpRequestContext.cs
│       └── Extensions/
│           ├── PresentationServiceCollectionExtensions.cs   # AddPresentation()
│           └── RateLimitPolicies.cs
│
└── tests/
    ├── Erp.Domain.UnitTests/
    ├── Erp.Application.UnitTests/
    ├── Erp.Api.IntegrationTests/
    └── Erp.ArchitectureTests/
```

**Referencias entre proyectos:**

| Proyecto | Referencia a |
|---|---|
| Erp.Domain | (ninguno) |
| Erp.Application | Erp.Domain |
| Erp.Infrastructure | Erp.Application |
| Erp.Api | Erp.Application, Erp.Infrastructure (solo para `AddInfrastructure`) |
| Tests | Según la capa que prueban |

---

## 5. Convenciones obligatorias

### 5.1 Código

| Elemento | Convención | Ejemplo |
|---|---|---|
| Namespace | File-scoped, igual a la carpeta | `namespace Erp.Application.Features.Auth.Login;` |
| Comando / Query | `{Verbo}{Entidad}Command` / `Get{Algo}Query` | `LoginCommand`, `GetCurrentUserQuery` |
| Handler | `{Comando}Handler`, `internal sealed` | `LoginCommandHandler` |
| Validador | `{Comando}Validator`, `internal sealed` | `LoginCommandValidator` |
| Request HTTP | `{Acción}Request` en `Api/Contracts` | `LoginRequest` |
| Respuesta | `{Algo}Response` | `AuthTokensResponse` |
| Config. EF | `{Entidad}Configuration` | `AppUserConfiguration` |
| Opciones | `{Tema}Options` + `SectionName` | `JwtOptions.SectionName = "Jwt"` |
| Errores | Clase estática `{Tema}Errors` | `AuthErrors.InvalidCredentials` |
| Campos privados | `_camelCase` | `_dbContext` |
| Async | Sufijo `Async` + `CancellationToken ct` | `SaveChangesAsync(ct)` |
| Tests | `Metodo_Escenario_ResultadoEsperado` | `Handle_WrongPassword_ReturnsInvalidCredentials` |

### 5.2 Reglas de código

- `<Nullable>enable</Nullable>` y `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>` en todos los proyectos.
- Clases `sealed` por defecto; `internal` todo lo que no necesite ser público.
- Primary constructors para inyección de dependencias.
- Errores esperados → `Result`. Excepciones solo para lo inesperado.
- Prohibido: lógica en controllers, `async void`, `.Result`/`.Wait()`, `catch` vacío, `DbContext` en controllers, devolver entidades como JSON, registrar contraseñas o tokens.
- Fechas en UTC con `DateTimeOffset` y `TimeProvider` (nunca `DateTime.Now`).
- JSON en `camelCase`; rutas en `kebab-case` y con versión: `/api/v1/auth/login`.

### 5.3 `Directory.Build.props`

```xml
<Project>
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <LangVersion>latest</LangVersion>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
    <AnalysisLevel>latest-recommended</AnalysisLevel>
    <EnforceCodeStyleInBuild>true</EnforceCodeStyleInBuild>
    <InvariantGlobalization>false</InvariantGlobalization>
  </PropertyGroup>
</Project>
```

> Si `AnalysisLevel latest-recommended` genera demasiadas advertencias en el cascarón, la IA puede suprimir reglas puntuales en `.editorconfig` justificando cada una.

---

## 6. Base de datos

### 6.1 Mapeo de la tabla existente `dbo.Usuarios` (solo lectura)

- Se mapea como entidad `SystemUser`, **excluida de migraciones** (`ExcludeFromMigrations`).
- Solo se mapean las columnas `Usuario`, `Contraseña`, `Nombre`, `TipoUsuario` y `Estatus`. **No** se mapean `Foto`, `Nota` ni `FechaAlta`, para no cargar el `image` y porque el usuario SQL solo tendrá permiso sobre esas columnas.
- La API **nunca escribe** en `dbo.Usuarios`.

### 6.2 Valores de `Estatus`

Los valores que significan "activo" son configurables (`Auth:ActiveStatuses`). **Verificar en la BD real** con:

```sql
SELECT Estatus, COUNT(*) FROM dbo.Usuarios GROUP BY Estatus;
SELECT TipoUsuario, COUNT(*) FROM dbo.Usuarios GROUP BY TipoUsuario;
```

y ajustar `appsettings.json` (por defecto `["ACTIVO"]`; la comparación ignora mayúsculas y espacios).

### 6.3 Esquema `seguridad` (referencia; lo genera la migración EF `InitialSecuritySchema`)

```sql
CREATE SCHEMA seguridad AUTHORIZATION dbo;
GO

CREATE TABLE seguridad.UsuariosApp (
    Usuario                nvarchar(20)   NOT NULL CONSTRAINT PK_UsuariosApp PRIMARY KEY,
    PasswordHash           nvarchar(256)  NULL,
    AccesoAppHabilitado    bit            NOT NULL,
    IntentosFallidos       int            NOT NULL,
    BloqueadoHastaUtc      datetimeoffset NULL,
    UltimoAccesoUtc        datetimeoffset NULL,
    PasswordActualizadoUtc datetimeoffset NULL,
    FechaCreacionUtc       datetimeoffset NOT NULL,
    RowVersion             rowversion     NOT NULL,
    CONSTRAINT FK_UsuariosApp_Usuarios FOREIGN KEY (Usuario)
        REFERENCES dbo.Usuarios (Usuario) ON DELETE CASCADE
);

CREATE TABLE seguridad.RefreshTokens (
    Id                 bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_RefreshTokens PRIMARY KEY,
    Usuario            nvarchar(20)     NOT NULL,
    TokenHash          char(64)         NOT NULL,
    FamiliaId          uniqueidentifier NOT NULL,
    DispositivoId      nvarchar(100)    NULL,
    FechaCreacionUtc   datetimeoffset   NOT NULL,
    ExpiraUtc          datetimeoffset   NOT NULL,
    RevocadoUtc        datetimeoffset   NULL,
    MotivoRevocacion   varchar(30)      NULL,
    ReemplazadoPorHash char(64)         NULL,
    IpCreacion         varchar(45)      NULL,
    UserAgent          nvarchar(256)    NULL,
    CONSTRAINT FK_RefreshTokens_Usuarios FOREIGN KEY (Usuario)
        REFERENCES dbo.Usuarios (Usuario) ON DELETE CASCADE
);
CREATE UNIQUE INDEX UX_RefreshTokens_TokenHash ON seguridad.RefreshTokens (TokenHash);
CREATE INDEX IX_RefreshTokens_Usuario   ON seguridad.RefreshTokens (Usuario);
CREATE INDEX IX_RefreshTokens_FamiliaId ON seguridad.RefreshTokens (FamiliaId);

CREATE TABLE seguridad.BitacoraAccesos (
    Id            bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_BitacoraAccesos PRIMARY KEY,
    Usuario       nvarchar(20)   NULL,     -- sin FK: registra también usuarios inexistentes
    FechaUtc      datetimeoffset NOT NULL,
    Evento        varchar(40)    NOT NULL,
    Exitoso       bit            NOT NULL,
    Ip            varchar(45)    NULL,
    DispositivoId nvarchar(100)  NULL,
    UserAgent     nvarchar(256)  NULL,
    Detalle       nvarchar(200)  NULL
);
CREATE INDEX IX_BitacoraAccesos_Usuario_FechaUtc ON seguridad.BitacoraAccesos (Usuario, FechaUtc DESC);
```

> `ON DELETE CASCADE`: si el sistema de escritorio elimina un usuario, se borran automáticamente su credencial de app y sus tokens. Esto no cambia el comportamiento del sistema de escritorio.

### 6.4 Usuarios SQL y permisos (archivo `database/001_seguridad_permisos.sql`)

Principio de **mínimo privilegio**: un usuario para la API en ejecución y otro solo para aplicar migraciones.

```sql
-- Ejecutar con un usuario administrador. Reemplazar contraseñas por valores seguros del gestor de secretos.
USE [master];
CREATE LOGIN erp_api      WITH PASSWORD = N'<<CAMBIAR>>', CHECK_POLICY = ON;
CREATE LOGIN erp_migrator WITH PASSWORD = N'<<CAMBIAR>>', CHECK_POLICY = ON;
GO
USE [Administracion];
CREATE USER erp_api      FOR LOGIN erp_api;
CREATE USER erp_migrator FOR LOGIN erp_migrator;
GO
-- API: solo lectura de las columnas necesarias de la tabla legada
GRANT SELECT ON dbo.Usuarios (Usuario, [Contraseña], Nombre, TipoUsuario, Estatus) TO erp_api;
-- API: lectura/escritura únicamente en el esquema propio (después de crear el esquema)
GRANT SELECT, INSERT, UPDATE, DELETE ON SCHEMA::seguridad TO erp_api;
GO
-- Migrador: crear objetos y referenciar dbo.Usuarios con FK
ALTER ROLE db_ddladmin ADD MEMBER erp_migrator;
GRANT REFERENCES ON dbo.Usuarios TO erp_migrator;
GRANT SELECT, INSERT, UPDATE, DELETE ON SCHEMA::dbo TO erp_migrator; -- tabla __EFMigrationsHistory
GO
```

### 6.5 Cadena de conexión

Nunca en `appsettings.json` con contraseña. En desarrollo usar **user-secrets**; en servidores, variables de entorno o un gestor de secretos.

```text
Server=SERVIDOR\INSTANCIA;Database=Administracion;User Id=erp_api;Password=***;Encrypt=True;TrustServerCertificate=False;Application Name=Erp.Api;
```

> `TrustServerCertificate=True` solo se permite en desarrollo local con un certificado autofirmado.

---

## 7. Contrato de API

### 7.1 Sobre de respuesta (idéntico al que consume la app móvil)

```json
{
  "success": true,
  "code": "OK",
  "message": null,
  "data": { },
  "errors": [],
  "traceId": "00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01",
  "timestamp": "2026-11-16T17:34:02Z"
}
```

### 7.2 Endpoints de la fase 1

| Método | Ruta | Auth | Rate limit | Descripción |
|---|---|---|---|---|
| POST | `/api/v1/auth/login` | Anónimo | `auth` (10/min por IP) | Inicia sesión |
| POST | `/api/v1/auth/refresh` | Anónimo | `auth` | Renueva tokens (rotación) |
| POST | `/api/v1/auth/logout` | Bearer | — | Revoca la sesión del dispositivo |
| GET | `/api/v1/auth/me` | Bearer | — | Perfil del usuario autenticado |
| GET | `/health/live` | Anónimo | — | Proceso vivo |
| GET | `/health/ready` | Anónimo | — | BD disponible |
| GET | `/openapi/v1.json`, `/scalar/v1`, `/swagger` | Anónimo | — | Documentación (solo si `OpenApi:Enabled = true`) |

### 7.3 Ejemplos

**POST `/api/v1/auth/login`**

```json
// Request
{ "userName": "jperez", "password": "MiClave123", "deviceId": "a1b2c3d4-android" }

// 200 OK
{
  "success": true,
  "code": "OK",
  "message": null,
  "data": {
    "tokenType": "Bearer",
    "accessToken": "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9...",
    "accessTokenExpiresAt": "2026-11-16T17:49:02+00:00",
    "refreshToken": "q4Zx...86 caracteres base64url...",
    "refreshTokenExpiresAt": "2026-11-30T17:34:02+00:00",
    "user": { "userName": "jperez", "name": "Juan Pérez", "userType": "VENDEDOR" }
  },
  "errors": [],
  "traceId": "00-...",
  "timestamp": "2026-11-16T17:34:02Z"
}

// 401
{ "success": false, "code": "AUTH_INVALID_CREDENTIALS", "message": "Usuario o contraseña incorrectos.", "data": null, "errors": [], "traceId": "00-...", "timestamp": "..." }

// 422
{
  "success": false, "code": "VALIDATION_ERROR", "message": "La información enviada no es válida.", "data": null,
  "errors": [ { "field": "userName", "code": "REQUIRED", "message": "El usuario es obligatorio." } ],
  "traceId": "00-...", "timestamp": "..."
}
```

**POST `/api/v1/auth/refresh`**: request `{ "refreshToken": "q4Zx..." }`, respuesta 200 con la misma forma que el login (tokens nuevos).

**POST `/api/v1/auth/logout`**: request `{ "refreshToken": "q4Zx..." }`, respuesta `{ "success": true, "code": "OK", "data": {} ... }`.

**GET `/api/v1/auth/me`**: `data: { "userName": "jperez", "name": "Juan Pérez", "userType": "VENDEDOR" }`.

### 7.4 Catálogo de códigos de error

| HTTP | `code` | Cuándo | Qué hace la app móvil |
|---|---|---|---|
| 401 | `AUTH_INVALID_CREDENTIALS` | Usuario inexistente o contraseña incorrecta (mismo mensaje en ambos casos) | Mostrar mensaje |
| 403 | `AUTH_ACCOUNT_LOCKED` | 5 intentos fallidos → bloqueo de 15 min | Mostrar minutos restantes |
| 403 | `AUTH_USER_INACTIVE` | `Estatus` no activo en `dbo.Usuarios` | Mostrar mensaje |
| 403 | `AUTH_APP_ACCESS_DISABLED` | `AccesoAppHabilitado = 0` | Mostrar mensaje |
| 401 | `AUTH_INVALID_REFRESH_TOKEN` | Refresh inválido, expirado, revocado o reutilizado | Cerrar sesión |
| 401 | `UNAUTHORIZED` | Sin token o token inválido | Intentar refresh |
| 401 | `TOKEN_EXPIRED` | Access token expirado | Intentar refresh |
| 403 | `FORBIDDEN` | Sin permiso | Mostrar mensaje |
| 400 | `BAD_REQUEST` | JSON mal formado | Error genérico (bug) |
| 422 | `VALIDATION_ERROR` | Reglas de formato | Errores por campo |
| 409 | `CONFLICT` | Concurrencia (dos primeros logins simultáneos, etc.) | Reintentar |
| 429 | `RATE_LIMITED` | Demasiadas peticiones | Esperar |
| 500 | `SERVER_ERROR` | Error inesperado | Mostrar folio (`traceId`) |

---

## 8. Código de referencia por capa

### 8.1 Domain

**`Common/Error.cs`**

```csharp
namespace Erp.Domain.Common;

public enum ErrorType { Failure, Validation, NotFound, Conflict, Unauthorized, Forbidden }

public record Error(string Code, string Message, ErrorType Type)
{
    public static readonly Error None = new(string.Empty, string.Empty, ErrorType.Failure);

    public static Error Failure(string code, string message) => new(code, message, ErrorType.Failure);
    public static Error NotFound(string code, string message) => new(code, message, ErrorType.NotFound);
    public static Error Conflict(string code, string message) => new(code, message, ErrorType.Conflict);
    public static Error Unauthorized(string code, string message) => new(code, message, ErrorType.Unauthorized);
    public static Error Forbidden(string code, string message) => new(code, message, ErrorType.Forbidden);
}

public sealed record FieldError(string Field, string Code, string Message);

public sealed record ValidationError(IReadOnlyList<FieldError> Errors)
    : Error("VALIDATION_ERROR", "La información enviada no es válida.", ErrorType.Validation);
```

**`Common/Result.cs`**

```csharp
namespace Erp.Domain.Common;

public class Result
{
    protected Result(bool isSuccess, Error error)
    {
        IsSuccess = isSuccess;
        Error = error;
    }

    public bool IsSuccess { get; }
    public bool IsFailure => !IsSuccess;
    public Error Error { get; }
}

public sealed class Result<T> : Result
{
    private readonly T? _value;

    private Result(T value) : base(true, Error.None) => _value = value;
    private Result(Error error) : base(false, error) { }

    public T Value => IsSuccess
        ? _value!
        : throw new InvalidOperationException("No se puede leer el valor de un resultado fallido.");

    public static Result<T> Success(T value) => new(value);
    public static Result<T> Failure(Error error) => new(error);

    public static implicit operator Result<T>(T value) => new(value);
    public static implicit operator Result<T>(Error error) => new(error);
}

/// <summary>Valor vacío para operaciones sin datos de retorno (serializa como {}).</summary>
public sealed record Success
{
    public static readonly Success Instance = new();
}
```

**`Users/SystemUser.cs`** (tabla legada, solo lectura)

```csharp
namespace Erp.Domain.Users;

/// <summary>Usuario del sistema de escritorio (dbo.Usuarios). Solo lectura para la API.</summary>
public sealed class SystemUser
{
    private SystemUser() { }

    public string UserName { get; private set; } = default!;
    /// <summary>Contraseña en texto plano del sistema legado. Solo se usa en el primer login.</summary>
    public string? LegacyPassword { get; private set; }
    public string? Name { get; private set; }
    public string? UserType { get; private set; }
    public string? Status { get; private set; }
}
```

**`Users/AppUser.cs`**

```csharp
namespace Erp.Domain.Users;

/// <summary>Credencial y estado de acceso a la app móvil (seguridad.UsuariosApp).</summary>
public sealed class AppUser
{
    private AppUser() { }

    public string UserName { get; private set; } = default!;
    public string? PasswordHash { get; private set; }
    public bool AppAccessEnabled { get; private set; }
    public int FailedAttempts { get; private set; }
    public DateTimeOffset? LockoutEndUtc { get; private set; }
    public DateTimeOffset? LastLoginUtc { get; private set; }
    public DateTimeOffset? PasswordChangedUtc { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public byte[] RowVersion { get; private set; } = [];

    public static AppUser Enroll(string userName, bool appAccessEnabled, DateTimeOffset now) => new()
    {
        UserName = userName,
        AppAccessEnabled = appAccessEnabled,
        CreatedAtUtc = now
    };

    public bool HasPassword => PasswordHash is not null;

    public bool IsLockedOut(DateTimeOffset now) => LockoutEndUtc is { } end && end > now;

    public void RegisterFailedAttempt(DateTimeOffset now, int maxAttempts, TimeSpan lockoutDuration)
    {
        FailedAttempts++;
        if (FailedAttempts < maxAttempts) return;

        LockoutEndUtc = now.Add(lockoutDuration);
        FailedAttempts = 0;
    }

    public void RegisterSuccessfulLogin(DateTimeOffset now)
    {
        FailedAttempts = 0;
        LockoutEndUtc = null;
        LastLoginUtc = now;
    }

    public void SetPasswordHash(string passwordHash, DateTimeOffset now)
    {
        PasswordHash = passwordHash;
        PasswordChangedUtc = now;
    }
}
```

**`Users/RefreshToken.cs`**

```csharp
namespace Erp.Domain.Users;

public sealed class RefreshToken
{
    private RefreshToken() { }

    public long Id { get; private set; }
    public string UserName { get; private set; } = default!;
    public string TokenHash { get; private set; } = default!;
    public Guid FamilyId { get; private set; }
    public string? DeviceId { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset ExpiresAtUtc { get; private set; }
    public DateTimeOffset? RevokedAtUtc { get; private set; }
    public string? RevocationReason { get; private set; }
    public string? ReplacedByTokenHash { get; private set; }
    public string? CreatedByIp { get; private set; }
    public string? UserAgent { get; private set; }

    public static RefreshToken Issue(
        string userName, string tokenHash, Guid familyId, string? deviceId,
        string? ip, string? userAgent, DateTimeOffset now, DateTimeOffset expiresAt) => new()
    {
        UserName = userName,
        TokenHash = tokenHash,
        FamilyId = familyId,
        DeviceId = deviceId,
        CreatedByIp = ip,
        UserAgent = userAgent,
        CreatedAtUtc = now,
        ExpiresAtUtc = expiresAt
    };

    public bool IsRevoked => RevokedAtUtc is not null;
    public bool IsExpired(DateTimeOffset now) => ExpiresAtUtc <= now;
    public bool IsActive(DateTimeOffset now) => !IsRevoked && !IsExpired(now);

    public void Revoke(DateTimeOffset now, string reason, string? replacedByTokenHash = null)
    {
        if (IsRevoked) return;
        RevokedAtUtc = now;
        RevocationReason = reason;
        ReplacedByTokenHash = replacedByTokenHash;
    }
}
```

**`Users/AccessLog.cs` y `Users/AccessEvents.cs`**

```csharp
namespace Erp.Domain.Users;

public sealed class AccessLog
{
    private AccessLog() { }

    public long Id { get; private set; }
    public string? UserName { get; private set; }
    public DateTimeOffset OccurredAtUtc { get; private set; }
    public string Event { get; private set; } = default!;
    public bool Succeeded { get; private set; }
    public string? IpAddress { get; private set; }
    public string? DeviceId { get; private set; }
    public string? UserAgent { get; private set; }
    public string? Detail { get; private set; }

    public static AccessLog Create(string? userName, string @event, bool succeeded, DateTimeOffset now,
        string? ip, string? deviceId, string? userAgent, string? detail = null) => new()
    {
        UserName = userName?.Length > 20 ? userName[..20] : userName,
        Event = @event,
        Succeeded = succeeded,
        OccurredAtUtc = now,
        IpAddress = ip,
        DeviceId = deviceId,
        UserAgent = userAgent?.Length > 256 ? userAgent[..256] : userAgent,
        Detail = detail?.Length > 200 ? detail[..200] : detail
    };
}

public static class AccessEvents
{
    public const string LoginSucceeded = "LoginSucceeded";
    public const string LoginFailed = "LoginFailed";
    public const string LoginBlocked = "LoginBlocked";
    public const string PasswordMigrated = "PasswordMigrated";
    public const string TokenRefreshed = "TokenRefreshed";
    public const string RefreshFailed = "RefreshFailed";
    public const string RefreshReuseDetected = "RefreshReuseDetected";
    public const string Logout = "Logout";
}
```

**`Users/AuthErrors.cs`**

```csharp
using Erp.Domain.Common;

namespace Erp.Domain.Users;

public static class AuthErrors
{
    public static readonly Error InvalidCredentials =
        Error.Unauthorized("AUTH_INVALID_CREDENTIALS", "Usuario o contraseña incorrectos.");

    public static readonly Error UserInactive =
        Error.Forbidden("AUTH_USER_INACTIVE", "El usuario está inactivo. Contacta al administrador.");

    public static readonly Error AppAccessDisabled =
        Error.Forbidden("AUTH_APP_ACCESS_DISABLED", "Tu usuario no tiene acceso a la aplicación móvil.");

    public static readonly Error InvalidRefreshToken =
        Error.Unauthorized("AUTH_INVALID_REFRESH_TOKEN", "La sesión expiró. Inicia sesión nuevamente.");

    public static Error AccountLocked(DateTimeOffset until, DateTimeOffset now)
    {
        var minutes = Math.Max(1, (int)Math.Ceiling((until - now).TotalMinutes));
        return Error.Forbidden("AUTH_ACCOUNT_LOCKED",
            $"Cuenta bloqueada temporalmente por intentos fallidos. Intenta de nuevo en {minutes} minuto(s).");
    }
}
```

### 8.2 Application

**`Abstractions/Messaging/ICommand.cs`**

```csharp
using Erp.Domain.Common;

namespace Erp.Application.Abstractions.Messaging;

public interface ICommand<TResponse>;
public interface IQuery<TResponse>;

public interface ICommandHandler<in TCommand, TResponse> where TCommand : ICommand<TResponse>
{
    Task<Result<TResponse>> Handle(TCommand command, CancellationToken ct);
}

public interface IQueryHandler<in TQuery, TResponse> where TQuery : IQuery<TResponse>
{
    Task<Result<TResponse>> Handle(TQuery query, CancellationToken ct);
}
```

**`Abstractions/Data/IApplicationDbContext.cs`**

```csharp
using Erp.Domain.Users;
using Microsoft.EntityFrameworkCore;

namespace Erp.Application.Abstractions.Data;

public interface IApplicationDbContext
{
    DbSet<SystemUser> SystemUsers { get; }
    DbSet<AppUser> AppUsers { get; }
    DbSet<RefreshToken> RefreshTokens { get; }
    DbSet<AccessLog> AccessLogs { get; }

    Task<int> SaveChangesAsync(CancellationToken ct = default);
}
```

**`Abstractions/Security/*`**

```csharp
namespace Erp.Application.Abstractions.Security;

public enum PasswordCheck { Failed, Success, SuccessRehashNeeded }

public interface IPasswordHasher
{
    string Hash(string password);
    PasswordCheck Verify(string passwordHash, string providedPassword);
    /// <summary>Ejecuta una verificación ficticia para igualar tiempos cuando el usuario no existe.</summary>
    void VerifyDummy(string providedPassword);
}

public interface ILegacyPasswordVerifier
{
    bool Verify(string? storedPlainPassword, string providedPassword);
}

public sealed record AccessToken(string Token, DateTimeOffset ExpiresAt);

public interface ITokenService
{
    AccessToken CreateAccessToken(string userName, string? name, string? userType, Guid sessionId);
}

public sealed record GeneratedRefreshToken(string Token, string Hash);

public interface IRefreshTokenFactory
{
    GeneratedRefreshToken Create();
    string ComputeHash(string token);
}

public interface ICurrentUser
{
    bool IsAuthenticated { get; }
    string UserName { get; }   // lanza si no está autenticado
}

public interface IRequestContext
{
    string? IpAddress { get; }
    string? UserAgent { get; }
}
```

**`Features/Auth/AuthOptions.cs`**

```csharp
using System.ComponentModel.DataAnnotations;

namespace Erp.Application.Features.Auth;

public sealed class AuthOptions
{
    public const string SectionName = "Auth";

    [Range(1, 20)] public int MaxFailedAttempts { get; init; } = 5;
    [Range(1, 1440)] public int LockoutMinutes { get; init; } = 15;
    [Range(1, 90)] public int RefreshTokenDays { get; init; } = 14;

    /// <summary>Si es true, cualquier usuario activo de dbo.Usuarios puede entrar a la app en su primer login.</summary>
    public bool AutoEnrollOnFirstLogin { get; init; } = true;

    /// <summary>Valores de dbo.Usuarios.Estatus que se consideran activos.</summary>
    [MinLength(1)] public string[] ActiveStatuses { get; init; } = [];

    public bool IsActiveStatus(string? status) =>
        status is not null &&
        ActiveStatuses.Any(s => string.Equals(s.Trim(), status.Trim(), StringComparison.OrdinalIgnoreCase));
}
```

**`Features/Auth/AuthTokensResponse.cs`**

```csharp
namespace Erp.Application.Features.Auth;

public sealed record UserProfileResponse(string UserName, string? Name, string? UserType);

public sealed record AuthTokensResponse(
    string AccessToken,
    DateTimeOffset AccessTokenExpiresAt,
    string RefreshToken,
    DateTimeOffset RefreshTokenExpiresAt,
    UserProfileResponse User)
{
    public string TokenType => "Bearer";
}
```

**`Features/Auth/Login/LoginCommand.cs` y validador**

```csharp
using Erp.Application.Abstractions.Messaging;
using FluentValidation;

namespace Erp.Application.Features.Auth.Login;

public sealed record LoginCommand(string UserName, string Password, string? DeviceId)
    : ICommand<AuthTokensResponse>;

internal sealed class LoginCommandValidator : AbstractValidator<LoginCommand>
{
    public LoginCommandValidator()
    {
        RuleFor(x => x.UserName)
            .NotEmpty().WithErrorCode("REQUIRED").WithMessage("El usuario es obligatorio.")
            .MaximumLength(20).WithErrorCode("MAX_LENGTH").WithMessage("El usuario no puede exceder 20 caracteres.");

        RuleFor(x => x.Password)
            .NotEmpty().WithErrorCode("REQUIRED").WithMessage("La contraseña es obligatoria.")
            .MaximumLength(128).WithErrorCode("MAX_LENGTH").WithMessage("La contraseña es demasiado larga.");

        RuleFor(x => x.DeviceId)
            .MaximumLength(100).WithErrorCode("MAX_LENGTH").WithMessage("El identificador de dispositivo es demasiado largo.");
    }
}
```

**`Features/Auth/Login/LoginCommandHandler.cs`** (núcleo de la fase 1)

```csharp
using Erp.Application.Abstractions.Data;
using Erp.Application.Abstractions.Messaging;
using Erp.Application.Abstractions.Security;
using Erp.Domain.Common;
using Erp.Domain.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Erp.Application.Features.Auth.Login;

internal sealed class LoginCommandHandler(
    IApplicationDbContext db,
    IPasswordHasher passwordHasher,
    ILegacyPasswordVerifier legacyPasswordVerifier,
    ITokenService tokenService,
    IRefreshTokenFactory refreshTokenFactory,
    IRequestContext requestContext,
    IOptions<AuthOptions> options,
    TimeProvider clock) : ICommandHandler<LoginCommand, AuthTokensResponse>
{
    public async Task<Result<AuthTokensResponse>> Handle(LoginCommand command, CancellationToken ct)
    {
        var opts = options.Value;
        var now = clock.GetUtcNow();
        var userName = command.UserName.Trim();

        // 1. Identidad desde la tabla legada (solo columnas mapeadas, sin Foto)
        var systemUser = await db.SystemUsers.AsNoTracking()
            .SingleOrDefaultAsync(u => u.UserName == userName, ct);

        if (systemUser is null)
        {
            passwordHasher.VerifyDummy(command.Password); // evita enumeración por tiempo de respuesta
            Log(userName, AccessEvents.LoginFailed, false, command.DeviceId, now, "Usuario inexistente");
            await db.SaveChangesAsync(ct);
            return AuthErrors.InvalidCredentials;
        }

        // 2. Credencial de la app (se crea en el primer intento para poder contar fallos)
        var appUser = await db.AppUsers.SingleOrDefaultAsync(a => a.UserName == systemUser.UserName, ct);
        if (appUser is null)
        {
            appUser = AppUser.Enroll(systemUser.UserName, opts.AutoEnrollOnFirstLogin, now);
            db.AppUsers.Add(appUser);
        }

        // 3. Bloqueo temporal
        if (appUser.IsLockedOut(now))
        {
            Log(systemUser.UserName, AccessEvents.LoginBlocked, false, command.DeviceId, now);
            await db.SaveChangesAsync(ct);
            return AuthErrors.AccountLocked(appUser.LockoutEndUtc!.Value, now);
        }

        // 4. Contraseña (hash o migración desde texto plano)
        if (!VerifyPassword(appUser, systemUser, command.Password, now, command.DeviceId))
        {
            appUser.RegisterFailedAttempt(now, opts.MaxFailedAttempts, TimeSpan.FromMinutes(opts.LockoutMinutes));
            Log(systemUser.UserName, AccessEvents.LoginFailed, false, command.DeviceId, now, "Contraseña incorrecta");
            await db.SaveChangesAsync(ct);
            return AuthErrors.InvalidCredentials;
        }

        // 5. Estado del usuario (se valida DESPUÉS de la contraseña para no revelar información)
        if (!opts.IsActiveStatus(systemUser.Status))
        {
            Log(systemUser.UserName, AccessEvents.LoginFailed, false, command.DeviceId, now, "Usuario inactivo");
            await db.SaveChangesAsync(ct);
            return AuthErrors.UserInactive;
        }

        if (!appUser.AppAccessEnabled)
        {
            Log(systemUser.UserName, AccessEvents.LoginFailed, false, command.DeviceId, now, "Acceso app deshabilitado");
            await db.SaveChangesAsync(ct);
            return AuthErrors.AppAccessDisabled;
        }

        // 6. Emisión de tokens
        appUser.RegisterSuccessfulLogin(now);

        var sessionId = Guid.NewGuid(); // = FamilyId de los refresh tokens de esta sesión
        var accessToken = tokenService.CreateAccessToken(systemUser.UserName, systemUser.Name, systemUser.UserType, sessionId);
        var refresh = refreshTokenFactory.Create();
        var refreshExpiresAt = now.AddDays(opts.RefreshTokenDays);

        db.RefreshTokens.Add(RefreshToken.Issue(
            systemUser.UserName, refresh.Hash, sessionId, command.DeviceId,
            requestContext.IpAddress, requestContext.UserAgent, now, refreshExpiresAt));

        Log(systemUser.UserName, AccessEvents.LoginSucceeded, true, command.DeviceId, now);
        await db.SaveChangesAsync(ct);

        return new AuthTokensResponse(
            accessToken.Token, accessToken.ExpiresAt, refresh.Token, refreshExpiresAt,
            new UserProfileResponse(systemUser.UserName, systemUser.Name, systemUser.UserType));
    }

    private bool VerifyPassword(AppUser appUser, SystemUser systemUser, string password, DateTimeOffset now, string? deviceId)
    {
        if (appUser.HasPassword)
        {
            var check = passwordHasher.Verify(appUser.PasswordHash!, password);
            if (check == PasswordCheck.SuccessRehashNeeded)
                appUser.SetPasswordHash(passwordHasher.Hash(password), now);
            return check != PasswordCheck.Failed;
        }

        // Primer acceso: valida contra dbo.Usuarios.Contraseña y migra a hash
        if (!legacyPasswordVerifier.Verify(systemUser.LegacyPassword, password))
            return false;

        appUser.SetPasswordHash(passwordHasher.Hash(password), now);
        Log(systemUser.UserName, AccessEvents.PasswordMigrated, true, deviceId, now);
        return true;
    }

    private void Log(string? userName, string @event, bool ok, string? deviceId, DateTimeOffset now, string? detail = null) =>
        db.AccessLogs.Add(AccessLog.Create(userName, @event, ok, now,
            requestContext.IpAddress, deviceId, requestContext.UserAgent, detail));
}
```

**`Features/Auth/RefreshToken/RefreshTokenCommandHandler.cs`**

```csharp
using Erp.Application.Abstractions.Data;
using Erp.Application.Abstractions.Messaging;
using Erp.Application.Abstractions.Security;
using Erp.Domain.Common;
using Erp.Domain.Users;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Erp.Application.Features.Auth.RefreshToken;

public sealed record RefreshTokenCommand(string RefreshToken) : ICommand<AuthTokensResponse>;

internal sealed class RefreshTokenCommandValidator : AbstractValidator<RefreshTokenCommand>
{
    public RefreshTokenCommandValidator() =>
        RuleFor(x => x.RefreshToken).NotEmpty().WithErrorCode("REQUIRED").MaximumLength(200).WithErrorCode("MAX_LENGTH");
}

internal sealed class RefreshTokenCommandHandler(
    IApplicationDbContext db,
    ITokenService tokenService,
    IRefreshTokenFactory refreshTokenFactory,
    IRequestContext requestContext,
    IOptions<AuthOptions> options,
    TimeProvider clock) : ICommandHandler<RefreshTokenCommand, AuthTokensResponse>
{
    public async Task<Result<AuthTokensResponse>> Handle(RefreshTokenCommand command, CancellationToken ct)
    {
        var opts = options.Value;
        var now = clock.GetUtcNow();
        var hash = refreshTokenFactory.ComputeHash(command.RefreshToken);

        var current = await db.RefreshTokens.SingleOrDefaultAsync(t => t.TokenHash == hash, ct);
        if (current is null)
            return await FailAsync(null, "Token inexistente", ct);

        // Reuso de un token ya rotado/revocado => posible robo: revocar toda la familia
        if (current.IsRevoked)
        {
            await db.RefreshTokens
                .Where(t => t.FamilyId == current.FamilyId && t.RevokedAtUtc == null)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(t => t.RevokedAtUtc, now)
                    .SetProperty(t => t.RevocationReason, "ReuseDetected"), ct);
            Log(current.UserName, AccessEvents.RefreshReuseDetected, false, current.DeviceId, now);
            await db.SaveChangesAsync(ct);
            return AuthErrors.InvalidRefreshToken;
        }

        if (current.IsExpired(now))
            return await FailAsync(current.UserName, "Token expirado", ct);

        // El usuario debe seguir activo y habilitado
        var systemUser = await db.SystemUsers.AsNoTracking().SingleOrDefaultAsync(u => u.UserName == current.UserName, ct);
        var appUser = await db.AppUsers.AsNoTracking().SingleOrDefaultAsync(a => a.UserName == current.UserName, ct);
        if (systemUser is null || appUser is null || !opts.IsActiveStatus(systemUser.Status)
            || !appUser.AppAccessEnabled || appUser.IsLockedOut(now))
        {
            current.Revoke(now, "UserNotAllowed");
            return await FailAsync(current.UserName, "Usuario no permitido", ct);
        }

        // Rotación
        var next = refreshTokenFactory.Create();
        var nextExpiresAt = now.AddDays(opts.RefreshTokenDays);
        current.Revoke(now, "Rotated", next.Hash);
        db.RefreshTokens.Add(Domain.Users.RefreshToken.Issue(
            current.UserName, next.Hash, current.FamilyId, current.DeviceId,
            requestContext.IpAddress, requestContext.UserAgent, now, nextExpiresAt));

        var access = tokenService.CreateAccessToken(systemUser.UserName, systemUser.Name, systemUser.UserType, current.FamilyId);
        Log(current.UserName, AccessEvents.TokenRefreshed, true, current.DeviceId, now);
        await db.SaveChangesAsync(ct);

        return new AuthTokensResponse(access.Token, access.ExpiresAt, next.Token, nextExpiresAt,
            new UserProfileResponse(systemUser.UserName, systemUser.Name, systemUser.UserType));
    }

    private async Task<Result<AuthTokensResponse>> FailAsync(string? userName, string detail, CancellationToken ct)
    {
        Log(userName, AccessEvents.RefreshFailed, false, null, clock.GetUtcNow(), detail);
        await db.SaveChangesAsync(ct);
        return AuthErrors.InvalidRefreshToken;
    }

    private void Log(string? userName, string @event, bool ok, string? deviceId, DateTimeOffset now, string? detail = null) =>
        db.AccessLogs.Add(AccessLog.Create(userName, @event, ok, now,
            requestContext.IpAddress, deviceId, requestContext.UserAgent, detail));
}
```

> Nota para la app móvil: dos refresh simultáneos con el mismo token provocan la detección de reuso y cierran la sesión. La app ya serializa el refresh con `SemaphoreSlim` (guía móvil, sección 13).

**`Features/Auth/Logout/LogoutCommandHandler.cs`**

```csharp
public sealed record LogoutCommand(string RefreshToken) : ICommand<Success>;

internal sealed class LogoutCommandValidator : AbstractValidator<LogoutCommand>
{
    public LogoutCommandValidator() =>
        RuleFor(x => x.RefreshToken).NotEmpty().WithErrorCode("REQUIRED").MaximumLength(200).WithErrorCode("MAX_LENGTH");
}

internal sealed class LogoutCommandHandler(
    IApplicationDbContext db, IRefreshTokenFactory factory, ICurrentUser currentUser,
    IRequestContext requestContext, TimeProvider clock) : ICommandHandler<LogoutCommand, Success>
{
    public async Task<Result<Success>> Handle(LogoutCommand command, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var hash = factory.ComputeHash(command.RefreshToken);

        var token = await db.RefreshTokens.SingleOrDefaultAsync(
            t => t.TokenHash == hash && t.UserName == currentUser.UserName, ct);

        // Idempotente: si no existe o ya está revocado, se responde éxito igualmente
        if (token is not null)
        {
            await db.RefreshTokens
                .Where(t => t.FamilyId == token.FamilyId && t.RevokedAtUtc == null)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(t => t.RevokedAtUtc, now)
                    .SetProperty(t => t.RevocationReason, "Logout"), ct);
        }

        db.AccessLogs.Add(AccessLog.Create(currentUser.UserName, AccessEvents.Logout, true, now,
            requestContext.IpAddress, token?.DeviceId, requestContext.UserAgent));
        await db.SaveChangesAsync(ct);
        return Success.Instance;
    }
}
```

**`Features/Auth/GetCurrentUser/GetCurrentUserQueryHandler.cs`**

```csharp
public sealed record GetCurrentUserQuery : IQuery<UserProfileResponse>;

internal sealed class GetCurrentUserQueryHandler(IApplicationDbContext db, ICurrentUser currentUser)
    : IQueryHandler<GetCurrentUserQuery, UserProfileResponse>
{
    public async Task<Result<UserProfileResponse>> Handle(GetCurrentUserQuery query, CancellationToken ct)
    {
        var profile = await db.SystemUsers.AsNoTracking()
            .Where(u => u.UserName == currentUser.UserName)
            .Select(u => new UserProfileResponse(u.UserName, u.Name, u.UserType))
            .SingleOrDefaultAsync(ct);

        return profile is null
            ? Error.NotFound("USER_NOT_FOUND", "El usuario ya no existe.")
            : profile;
    }
}
```

**`Behaviors/ValidationCommandHandlerDecorator.cs`**

```csharp
using Erp.Application.Abstractions.Messaging;
using Erp.Domain.Common;
using FluentValidation;
using System.Text.Json;

namespace Erp.Application.Behaviors;

internal sealed class ValidationCommandHandlerDecorator<TCommand, TResponse>(
    ICommandHandler<TCommand, TResponse> inner,
    IEnumerable<IValidator<TCommand>> validators) : ICommandHandler<TCommand, TResponse>
    where TCommand : ICommand<TResponse>
{
    public async Task<Result<TResponse>> Handle(TCommand command, CancellationToken ct)
    {
        var failures = new List<FieldError>();
        foreach (var validator in validators)
        {
            var result = await validator.ValidateAsync(command, ct);
            failures.AddRange(result.Errors.Select(f => new FieldError(
                JsonNamingPolicy.CamelCase.ConvertName(f.PropertyName), f.ErrorCode, f.ErrorMessage)));
        }

        return failures.Count == 0
            ? await inner.Handle(command, ct)
            : new ValidationError(failures);
    }
}
```

**`Behaviors/LoggingCommandHandlerDecorator.cs`**

```csharp
using System.Diagnostics;
using Erp.Application.Abstractions.Messaging;
using Erp.Domain.Common;
using Microsoft.Extensions.Logging;

namespace Erp.Application.Behaviors;

internal sealed class LoggingCommandHandlerDecorator<TCommand, TResponse>(
    ICommandHandler<TCommand, TResponse> inner,
    ILogger<LoggingCommandHandlerDecorator<TCommand, TResponse>> logger) : ICommandHandler<TCommand, TResponse>
    where TCommand : ICommand<TResponse>
{
    public async Task<Result<TResponse>> Handle(TCommand command, CancellationToken ct)
    {
        var name = typeof(TCommand).Name;          // NUNCA registrar el contenido del comando (contiene contraseñas)
        var sw = Stopwatch.StartNew();
        var result = await inner.Handle(command, ct);
        sw.Stop();

        if (result.IsFailure)
            logger.LogWarning("{Command} falló con {ErrorCode} en {ElapsedMs} ms", name, result.Error.Code, sw.ElapsedMilliseconds);
        else
            logger.LogInformation("{Command} completado en {ElapsedMs} ms", name, sw.ElapsedMilliseconds);

        return result;
    }
}
```

**`DependencyInjection.cs` (Application)**

```csharp
using Erp.Application.Abstractions.Messaging;
using Erp.Application.Behaviors;
using Erp.Application.Features.Auth;
using FluentValidation;
using Microsoft.Extensions.DependencyInjection;

namespace Erp.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        var assembly = typeof(DependencyInjection).Assembly;

        services.Scan(scan => scan.FromAssemblies(assembly)
            .AddClasses(c => c.AssignableTo(typeof(ICommandHandler<,>)).Where(t => !t.Name.Contains("Decorator")), publicOnly: false)
                .AsImplementedInterfaces().WithScopedLifetime()
            .AddClasses(c => c.AssignableTo(typeof(IQueryHandler<,>)), publicOnly: false)
                .AsImplementedInterfaces().WithScopedLifetime());

        // Orden: el último decorador registrado es el más externo (Logging -> Validation -> Handler)
        services.Decorate(typeof(ICommandHandler<,>), typeof(ValidationCommandHandlerDecorator<,>));
        services.Decorate(typeof(ICommandHandler<,>), typeof(LoggingCommandHandlerDecorator<,>));

        services.AddValidatorsFromAssembly(assembly, includeInternalTypes: true);

        services.AddOptions<AuthOptions>()
            .BindConfiguration(AuthOptions.SectionName)
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddSingleton(TimeProvider.System);
        return services;
    }
}
```

### 8.3 Infrastructure

**`Persistence/ErpDbContext.cs`**

```csharp
using Erp.Application.Abstractions.Data;
using Erp.Domain.Users;
using Microsoft.EntityFrameworkCore;

namespace Erp.Infrastructure.Persistence;

public sealed class ErpDbContext(DbContextOptions<ErpDbContext> options) : DbContext(options), IApplicationDbContext
{
    public DbSet<SystemUser> SystemUsers => Set<SystemUser>();
    public DbSet<AppUser> AppUsers => Set<AppUser>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<AccessLog> AccessLogs => Set<AccessLog>();

    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ErpDbContext).Assembly);
}
```

**`Persistence/Configurations/SystemUserConfiguration.cs`**

```csharp
internal sealed class SystemUserConfiguration : IEntityTypeConfiguration<SystemUser>
{
    public void Configure(EntityTypeBuilder<SystemUser> b)
    {
        // Tabla del sistema de escritorio: NUNCA se crea ni modifica por migraciones
        b.ToTable("Usuarios", "dbo", t => t.ExcludeFromMigrations());
        b.HasKey(u => u.UserName);

        b.Property(u => u.UserName).HasColumnName("Usuario").HasMaxLength(20);
        b.Property(u => u.LegacyPassword).HasColumnName("Contraseña").HasMaxLength(20);
        b.Property(u => u.Name).HasColumnName("Nombre").HasMaxLength(50);
        b.Property(u => u.UserType).HasColumnName("TipoUsuario").HasMaxLength(20).IsUnicode(false);
        b.Property(u => u.Status).HasColumnName("Estatus").HasMaxLength(10).IsUnicode(false);
        // Foto, Nota y FechaAlta NO se mapean a propósito.
    }
}
```

**`Persistence/Configurations/AppUserConfiguration.cs`**

```csharp
internal sealed class AppUserConfiguration : IEntityTypeConfiguration<AppUser>
{
    public void Configure(EntityTypeBuilder<AppUser> b)
    {
        b.ToTable("UsuariosApp", "seguridad");
        b.HasKey(a => a.UserName).HasName("PK_UsuariosApp");

        b.Property(a => a.UserName).HasColumnName("Usuario").HasMaxLength(20);
        b.Property(a => a.PasswordHash).HasColumnName("PasswordHash").HasMaxLength(256);
        b.Property(a => a.AppAccessEnabled).HasColumnName("AccesoAppHabilitado");
        b.Property(a => a.FailedAttempts).HasColumnName("IntentosFallidos");
        b.Property(a => a.LockoutEndUtc).HasColumnName("BloqueadoHastaUtc");
        b.Property(a => a.LastLoginUtc).HasColumnName("UltimoAccesoUtc");
        b.Property(a => a.PasswordChangedUtc).HasColumnName("PasswordActualizadoUtc");
        b.Property(a => a.CreatedAtUtc).HasColumnName("FechaCreacionUtc");
        b.Property(a => a.RowVersion).IsRowVersion();
        b.Ignore(a => a.HasPassword);

        b.HasOne<SystemUser>().WithOne()
            .HasForeignKey<AppUser>(a => a.UserName)
            .HasConstraintName("FK_UsuariosApp_Usuarios")
            .OnDelete(DeleteBehavior.Cascade);
    }
}
```

**`Persistence/Configurations/RefreshTokenConfiguration.cs`**

```csharp
internal sealed class RefreshTokenConfiguration : IEntityTypeConfiguration<RefreshToken>
{
    public void Configure(EntityTypeBuilder<RefreshToken> b)
    {
        b.ToTable("RefreshTokens", "seguridad");
        b.HasKey(t => t.Id).HasName("PK_RefreshTokens");

        b.Property(t => t.UserName).HasColumnName("Usuario").HasMaxLength(20);
        b.Property(t => t.TokenHash).HasMaxLength(64).IsFixedLength().IsUnicode(false);
        b.Property(t => t.FamilyId).HasColumnName("FamiliaId");
        b.Property(t => t.DeviceId).HasColumnName("DispositivoId").HasMaxLength(100);
        b.Property(t => t.CreatedAtUtc).HasColumnName("FechaCreacionUtc");
        b.Property(t => t.ExpiresAtUtc).HasColumnName("ExpiraUtc");
        b.Property(t => t.RevokedAtUtc).HasColumnName("RevocadoUtc");
        b.Property(t => t.RevocationReason).HasColumnName("MotivoRevocacion").HasMaxLength(30).IsUnicode(false);
        b.Property(t => t.ReplacedByTokenHash).HasColumnName("ReemplazadoPorHash").HasMaxLength(64).IsFixedLength().IsUnicode(false);
        b.Property(t => t.CreatedByIp).HasColumnName("IpCreacion").HasMaxLength(45).IsUnicode(false);
        b.Property(t => t.UserAgent).HasMaxLength(256);

        b.HasIndex(t => t.TokenHash).IsUnique().HasDatabaseName("UX_RefreshTokens_TokenHash");
        b.HasIndex(t => t.UserName).HasDatabaseName("IX_RefreshTokens_Usuario");
        b.HasIndex(t => t.FamilyId).HasDatabaseName("IX_RefreshTokens_FamiliaId");

        b.HasOne<SystemUser>().WithMany()
            .HasForeignKey(t => t.UserName)
            .HasConstraintName("FK_RefreshTokens_Usuarios")
            .OnDelete(DeleteBehavior.Cascade);
    }
}
```

**`Persistence/Configurations/AccessLogConfiguration.cs`**: tabla `seguridad.BitacoraAccesos`, columnas según la sección 6.3 (`Usuario`, `FechaUtc`, `Evento` varchar(40), `Exitoso`, `Ip` varchar(45), `DispositivoId`, `UserAgent`, `Detalle`), índice `IX_BitacoraAccesos_Usuario_FechaUtc` y **sin FK**.

**`Security/PasswordHasherService.cs`**

```csharp
using Erp.Application.Abstractions.Security;
using Erp.Domain.Users;
using Microsoft.AspNetCore.Identity;

namespace Erp.Infrastructure.Security;

/// <summary>PBKDF2 (formato V3 de ASP.NET Core Identity) con sal aleatoria por contraseña.</summary>
internal sealed class PasswordHasherService : IPasswordHasher
{
    private static readonly PasswordHasher<AppUser> Hasher = new();
    private static readonly Lazy<string> DummyHash = new(() => Hasher.HashPassword(null!, Guid.NewGuid().ToString("N")));

    public string Hash(string password) => Hasher.HashPassword(null!, password);

    public PasswordCheck Verify(string passwordHash, string providedPassword) =>
        Hasher.VerifyHashedPassword(null!, passwordHash, providedPassword) switch
        {
            PasswordVerificationResult.Success => PasswordCheck.Success,
            PasswordVerificationResult.SuccessRehashNeeded => PasswordCheck.SuccessRehashNeeded,
            _ => PasswordCheck.Failed
        };

    public void VerifyDummy(string providedPassword) =>
        _ = Hasher.VerifyHashedPassword(null!, DummyHash.Value, providedPassword);
}
```

**`Security/LegacyPasswordVerifier.cs`**

```csharp
using System.Security.Cryptography;
using System.Text;
using Erp.Application.Abstractions.Security;

namespace Erp.Infrastructure.Security;

/// <summary>
/// Compara contra la contraseña en texto plano de dbo.Usuarios en tiempo constante.
/// Solo se usa en el primer login de cada usuario (migración a hash).
/// VERIFICAR con el sistema de escritorio si su comparación distingue mayúsculas o recorta espacios
/// y replicar exactamente ese comportamiento aquí.
/// </summary>
internal sealed class LegacyPasswordVerifier : ILegacyPasswordVerifier
{
    public bool Verify(string? storedPlainPassword, string providedPassword)
    {
        if (string.IsNullOrEmpty(storedPlainPassword)) return false; // usuarios sin contraseña no entran

        var stored = Encoding.UTF8.GetBytes(storedPlainPassword);
        var provided = Encoding.UTF8.GetBytes(providedPassword);
        return CryptographicOperations.FixedTimeEquals(stored, provided);
    }
}
```

**`Security/JwtOptions.cs` y `Security/JwtTokenService.cs`**

```csharp
using System.ComponentModel.DataAnnotations;

namespace Erp.Infrastructure.Security;

public sealed class JwtOptions
{
    public const string SectionName = "Jwt";

    [Required] public string Issuer { get; init; } = default!;
    [Required] public string Audience { get; init; } = default!;
    /// <summary>Mínimo 32 caracteres (256 bits). Viene de user-secrets / variables de entorno / bóveda.</summary>
    [Required, MinLength(32)] public string SigningKey { get; init; } = default!;
    [Range(5, 60)] public int AccessTokenMinutes { get; init; } = 15;
}
```

```csharp
using System.Security.Claims;
using System.Text;
using Erp.Application.Abstractions.Security;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Erp.Infrastructure.Security;

internal sealed class JwtTokenService(IOptions<JwtOptions> options, TimeProvider clock) : ITokenService
{
    private static readonly JsonWebTokenHandler Handler = new();

    public AccessToken CreateAccessToken(string userName, string? name, string? userType, Guid sessionId)
    {
        var jwt = options.Value;
        var now = clock.GetUtcNow();
        var expires = now.AddMinutes(jwt.AccessTokenMinutes);

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, userName),
            new(JwtRegisteredClaimNames.Name, name ?? userName),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new(JwtRegisteredClaimNames.Sid, sessionId.ToString())
        };
        if (!string.IsNullOrWhiteSpace(userType))
            claims.Add(new Claim("role", userType.Trim()));

        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = jwt.Issuer,
            Audience = jwt.Audience,
            IssuedAt = now.UtcDateTime,
            NotBefore = now.UtcDateTime,
            Expires = expires.UtcDateTime,
            Subject = new ClaimsIdentity(claims),
            SigningCredentials = new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.SigningKey)), SecurityAlgorithms.HmacSha256)
        };

        return new AccessToken(Handler.CreateToken(descriptor), expires);
    }
}
```

**`Security/RefreshTokenFactory.cs`**

```csharp
using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using Erp.Application.Abstractions.Security;

namespace Erp.Infrastructure.Security;

internal sealed class RefreshTokenFactory : IRefreshTokenFactory
{
    public GeneratedRefreshToken Create()
    {
        var token = Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(64)); // 86 caracteres
        return new GeneratedRefreshToken(token, ComputeHash(token));
    }

    public string ComputeHash(string token) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token))); // 64 caracteres
}
```

**`DependencyInjection.cs` (Infrastructure)**

```csharp
using Erp.Application.Abstractions.Data;
using Erp.Application.Abstractions.Security;
using Erp.Infrastructure.Persistence;
using Erp.Infrastructure.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Erp.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration config)
    {
        var connectionString = config.GetConnectionString("Administracion")
            ?? throw new InvalidOperationException("Falta ConnectionStrings:Administracion.");

        services.AddDbContext<ErpDbContext>(o => o.UseSqlServer(connectionString, sql =>
        {
            sql.EnableRetryOnFailure(maxRetryCount: 3);
            sql.CommandTimeout(30);
            sql.MigrationsHistoryTable("__EFMigrationsHistory", "seguridad");
        }));
        services.AddScoped<IApplicationDbContext>(sp => sp.GetRequiredService<ErpDbContext>());

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
```

> El historial de migraciones va en el esquema `seguridad` para no tocar `dbo`. Si se usa esta opción, el permiso `GRANT ... ON SCHEMA::dbo TO erp_migrator` de la sección 6.4 deja de ser necesario.

### 8.4 Api

**`Contracts/ApiResponse.cs`**

```csharp
using System.Text.Json.Serialization;
using Erp.Domain.Common;

namespace Erp.Api.Contracts;

public sealed record ApiResponse<T>
{
    public bool Success { get; init; }
    public string Code { get; init; } = "OK";
    public string? Message { get; init; }
    public T? Data { get; init; }
    public IReadOnlyList<FieldError> Errors { get; init; } = [];
    public string? TraceId { get; init; }
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>Solo en Development: detalle técnico de excepciones.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Detail { get; init; }
}

public static class ApiResponse
{
    public static ApiResponse<T> Ok<T>(T data, string? traceId, string code = "OK") =>
        new() { Success = true, Code = code, Data = data, TraceId = traceId };

    public static ApiResponse<object> Fail(Error error, string? traceId) => new()
    {
        Success = false,
        Code = error.Code,
        Message = error.Message,
        Errors = error is ValidationError v ? v.Errors : [],
        TraceId = traceId
    };

    public static ApiResponse<object> Fail(string code, string message, string? traceId,
        IReadOnlyList<FieldError>? errors = null, string? detail = null) => new()
    {
        Success = false, Code = code, Message = message, TraceId = traceId, Errors = errors ?? [], Detail = detail
    };
}
```

**`Controllers/ApiControllerBase.cs`**

```csharp
using System.Diagnostics;
using Erp.Api.Contracts;
using Erp.Domain.Common;
using Microsoft.AspNetCore.Mvc;

namespace Erp.Api.Controllers;

[ApiController]
[Produces("application/json")]
public abstract class ApiControllerBase : ControllerBase
{
    protected string TraceId => Activity.Current?.Id ?? HttpContext.TraceIdentifier;

    protected IActionResult HandleResult<T>(Result<T> result) =>
        result.IsSuccess ? Ok(ApiResponse.Ok(result.Value, TraceId)) : Failure(result.Error);

    private ObjectResult Failure(Error error)
    {
        var status = error.Type switch
        {
            ErrorType.Validation => StatusCodes.Status422UnprocessableEntity,
            ErrorType.NotFound => StatusCodes.Status404NotFound,
            ErrorType.Conflict => StatusCodes.Status409Conflict,
            ErrorType.Unauthorized => StatusCodes.Status401Unauthorized,
            ErrorType.Forbidden => StatusCodes.Status403Forbidden,
            _ => StatusCodes.Status400BadRequest
        };
        return StatusCode(status, ApiResponse.Fail(error, TraceId));
    }
}
```

**`Contracts/Auth/*.cs`**

```csharp
namespace Erp.Api.Contracts.Auth;

/// <summary>Credenciales de inicio de sesión.</summary>
/// <param name="UserName">Usuario del sistema (máx. 20 caracteres).</param>
/// <param name="Password">Contraseña.</param>
/// <param name="DeviceId">Identificador del dispositivo móvil (opcional, recomendado).</param>
public sealed record LoginRequest(string UserName, string Password, string? DeviceId)
{
    public LoginCommand ToCommand() => new(UserName ?? string.Empty, Password ?? string.Empty, DeviceId);
}

/// <summary>Refresh token emitido en el login o en el último refresh.</summary>
public sealed record RefreshTokenRequest(string RefreshToken)
{
    public RefreshTokenCommand ToCommand() => new(RefreshToken ?? string.Empty);
}

/// <summary>Refresh token de la sesión a cerrar.</summary>
public sealed record LogoutRequest(string RefreshToken)
{
    public LogoutCommand ToCommand() => new(RefreshToken ?? string.Empty);
}
```

**`Controllers/V1/AuthController.cs`** (documentado para OpenAPI)

```csharp
using Erp.Api.Contracts;
using Erp.Api.Contracts.Auth;
using Erp.Api.Extensions;
using Erp.Application.Abstractions.Messaging;
using Erp.Application.Features.Auth;
using Erp.Application.Features.Auth.GetCurrentUser;
using Erp.Application.Features.Auth.Login;
using Erp.Application.Features.Auth.Logout;
using Erp.Application.Features.Auth.RefreshToken;
using Erp.Domain.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Erp.Api.Controllers.V1;

/// <summary>Autenticación de usuarios de la aplicación móvil.</summary>
[Route("api/v1/auth")]
[Tags("Autenticación")]
public sealed class AuthController : ApiControllerBase
{
    /// <summary>Inicia sesión con usuario y contraseña.</summary>
    /// <remarks>
    /// Valida al usuario contra la tabla de usuarios del sistema. En el primer acceso desde la app,
    /// la contraseña se migra a un hash seguro. Tras 5 intentos fallidos la cuenta se bloquea 15 minutos.
    /// </remarks>
    /// <response code="200">Sesión iniciada. Devuelve access token, refresh token y perfil.</response>
    /// <response code="401">Usuario o contraseña incorrectos (AUTH_INVALID_CREDENTIALS).</response>
    /// <response code="403">Cuenta bloqueada, usuario inactivo o sin acceso a la app.</response>
    /// <response code="422">Datos de entrada inválidos.</response>
    /// <response code="429">Demasiados intentos desde la misma IP.</response>
    [HttpPost("login")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitPolicies.Auth)]
    [ProducesResponseType<ApiResponse<AuthTokensResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse<object>>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ApiResponse<object>>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ApiResponse<object>>(StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType<ApiResponse<object>>(StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> Login(
        [FromBody] LoginRequest request,
        [FromServices] ICommandHandler<LoginCommand, AuthTokensResponse> handler,
        CancellationToken ct) =>
        HandleResult(await handler.Handle(request.ToCommand(), ct));

    /// <summary>Renueva el access token usando un refresh token (rotación).</summary>
    /// <remarks>El refresh token usado queda revocado. Reusar un token revocado cierra todas las sesiones derivadas.</remarks>
    /// <response code="200">Tokens nuevos.</response>
    /// <response code="401">Refresh token inválido, expirado o revocado (AUTH_INVALID_REFRESH_TOKEN).</response>
    [HttpPost("refresh")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitPolicies.Auth)]
    [ProducesResponseType<ApiResponse<AuthTokensResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse<object>>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ApiResponse<object>>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Refresh(
        [FromBody] RefreshTokenRequest request,
        [FromServices] ICommandHandler<RefreshTokenCommand, AuthTokensResponse> handler,
        CancellationToken ct) =>
        HandleResult(await handler.Handle(request.ToCommand(), ct));

    /// <summary>Cierra la sesión del dispositivo actual.</summary>
    /// <response code="200">Sesión cerrada (operación idempotente).</response>
    /// <response code="401">No autenticado.</response>
    [HttpPost("logout")]
    [ProducesResponseType<ApiResponse<Success>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse<object>>(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Logout(
        [FromBody] LogoutRequest request,
        [FromServices] ICommandHandler<LogoutCommand, Success> handler,
        CancellationToken ct) =>
        HandleResult(await handler.Handle(request.ToCommand(), ct));

    /// <summary>Devuelve el perfil del usuario autenticado.</summary>
    /// <response code="200">Perfil del usuario.</response>
    /// <response code="401">No autenticado o token expirado.</response>
    [HttpGet("me")]
    [ProducesResponseType<ApiResponse<UserProfileResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse<object>>(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Me(
        [FromServices] IQueryHandler<GetCurrentUserQuery, UserProfileResponse> handler,
        CancellationToken ct) =>
        HandleResult(await handler.Handle(new GetCurrentUserQuery(), ct));
}
```

**`ErrorHandling/GlobalExceptionHandler.cs`**

```csharp
using System.Diagnostics;
using Erp.Api.Contracts;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.EntityFrameworkCore;

namespace Erp.Api.ErrorHandling;

internal sealed class GlobalExceptionHandler(
    ILogger<GlobalExceptionHandler> logger, IHostEnvironment env) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception, CancellationToken ct)
    {
        var (status, code, message) = exception switch
        {
            DbUpdateConcurrencyException => (StatusCodes.Status409Conflict, "CONFLICT",
                "El registro fue modificado por otra operación. Intenta de nuevo."),
            DbUpdateException => (StatusCodes.Status409Conflict, "CONFLICT",
                "No fue posible completar la operación. Intenta de nuevo."),
            BadHttpRequestException => (StatusCodes.Status400BadRequest, "BAD_REQUEST",
                "La petición no tiene un formato válido."),
            OperationCanceledException => (499, "REQUEST_CANCELLED", "La petición fue cancelada."),
            _ => (StatusCodes.Status500InternalServerError, "SERVER_ERROR",
                "Ocurrió un error inesperado. Contacta a soporte con el folio.")
        };

        if (status >= 500)
            logger.LogError(exception, "Excepción no controlada en {Method} {Path}", context.Request.Method, context.Request.Path);
        else
            logger.LogWarning(exception, "Excepción controlada {Code} en {Path}", code, context.Request.Path);

        var traceId = Activity.Current?.Id ?? context.TraceIdentifier;
        context.Response.StatusCode = status;
        await context.Response.WriteAsJsonAsync(
            ApiResponse.Fail(code, message, traceId, detail: env.IsDevelopment() ? exception.ToString() : null), ct);
        return true;
    }
}
```

**`ErrorHandling/AuthResponseWriter.cs`** (401/403 del middleware JWT con el sobre estándar)

```csharp
using System.Diagnostics;
using Erp.Api.Contracts;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;

namespace Erp.Api.ErrorHandling;

internal static class AuthResponseWriter
{
    public static async Task WriteChallengeAsync(JwtBearerChallengeContext ctx)
    {
        ctx.HandleResponse();
        var expired = ctx.AuthenticateFailure is SecurityTokenExpiredException;
        ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
        if (expired) ctx.Response.Headers["X-Token-Expired"] = "true";

        await ctx.Response.WriteAsJsonAsync(ApiResponse.Fail(
            expired ? "TOKEN_EXPIRED" : "UNAUTHORIZED",
            expired ? "La sesión expiró." : "Debes iniciar sesión.",
            Activity.Current?.Id ?? ctx.HttpContext.TraceIdentifier));
    }

    public static async Task WriteForbiddenAsync(ForbiddenContext ctx)
    {
        ctx.Response.StatusCode = StatusCodes.Status403Forbidden;
        await ctx.Response.WriteAsJsonAsync(ApiResponse.Fail("FORBIDDEN",
            "No tienes permiso para esta operación.", Activity.Current?.Id ?? ctx.HttpContext.TraceIdentifier));
    }
}
```

**`OpenApi/BearerSecuritySchemeTransformer.cs`** (habilita el botón "Authorize" en Scalar/Swagger)

```csharp
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi; // .NET 10 usa Microsoft.OpenApi 2.x (en .NET 9 era Microsoft.OpenApi.Models)

namespace Erp.Api.OpenApi;

internal sealed class BearerSecuritySchemeTransformer : IOpenApiDocumentTransformer
{
    public Task TransformAsync(OpenApiDocument document, OpenApiDocumentTransformerContext context, CancellationToken ct)
    {
        document.Components ??= new OpenApiComponents();
        document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();
        document.Components.SecuritySchemes["Bearer"] = new OpenApiSecurityScheme
        {
            Type = SecuritySchemeType.Http,
            Scheme = "bearer",
            BearerFormat = "JWT",
            In = ParameterLocation.Header,
            Description = "Access token obtenido en POST /api/v1/auth/login"
        };

        document.Security ??= [];
        document.Security.Add(new OpenApiSecurityRequirement
        {
            [new OpenApiSecuritySchemeReference("Bearer", document)] = []
        });
        return Task.CompletedTask;
    }
}
```

> Si la versión instalada de `Microsoft.OpenApi` cambia nombres de tipos, la IA debe ajustar este archivo (marcar con `// AJUSTE:`) manteniendo el objetivo: esquema Bearer global en el documento.

**`OpenApi/OpenApiExtensions.cs`**

```csharp
using Erp.Api.OpenApi;
using Scalar.AspNetCore;

namespace Erp.Api.OpenApi;

internal static class OpenApiExtensions
{
    public static IServiceCollection AddApiDocumentation(this IServiceCollection services)
    {
        services.AddOpenApi("v1", options =>
        {
            options.AddDocumentTransformer((doc, _, _) =>
            {
                doc.Info.Title = "ERP Administración — API";
                doc.Info.Version = "v1";
                doc.Info.Description = "API para la aplicación móvil del ERP. Todas las respuestas usan el sobre ApiResponse<T>.";
                return Task.CompletedTask;
            });
            options.AddDocumentTransformer<BearerSecuritySchemeTransformer>();
        });
        return services;
    }

    public static WebApplication UseApiDocumentation(this WebApplication app)
    {
        if (!app.Configuration.GetValue<bool>("OpenApi:Enabled")) return app;

        app.MapOpenApi();                                         // /openapi/v1.json
        app.MapScalarApiReference(o => o.WithTitle("ERP API"));   // /scalar/v1
        app.UseSwaggerUI(o =>                                     // /swagger
        {
            o.SwaggerEndpoint("/openapi/v1.json", "ERP API v1");
            o.DocumentTitle = "ERP API";
        });
        return app;
    }
}
```

En el `.csproj` de **Erp.Api** (para que los comentarios XML aparezcan en la documentación):

```xml
<PropertyGroup>
  <GenerateDocumentationFile>true</GenerateDocumentationFile>
  <NoWarn>$(NoWarn);1591</NoWarn>
  <UserSecretsId>erp-api-administracion</UserSecretsId>
</PropertyGroup>
```

**`Services/CurrentUser.cs` y `Services/HttpRequestContext.cs`**

```csharp
using System.Security.Claims;
using Erp.Application.Abstractions.Security;
using Microsoft.IdentityModel.JsonWebTokens;

namespace Erp.Api.Services;

internal sealed class CurrentUser(IHttpContextAccessor accessor) : ICurrentUser
{
    private ClaimsPrincipal? Principal => accessor.HttpContext?.User;

    public bool IsAuthenticated => Principal?.Identity?.IsAuthenticated == true;

    public string UserName => Principal?.FindFirstValue(JwtRegisteredClaimNames.Sub)
        ?? throw new InvalidOperationException("No hay un usuario autenticado en el contexto.");
}

internal sealed class HttpRequestContext(IHttpContextAccessor accessor) : IRequestContext
{
    public string? IpAddress => accessor.HttpContext?.Connection.RemoteIpAddress?.ToString();

    public string? UserAgent
    {
        get
        {
            var ua = accessor.HttpContext?.Request.Headers.UserAgent.ToString();
            return string.IsNullOrWhiteSpace(ua) ? null : ua.Length > 256 ? ua[..256] : ua;
        }
    }
}
```

**`Middleware/SecurityHeadersMiddleware.cs`**

```csharp
namespace Erp.Api.Middleware;

internal sealed class SecurityHeadersMiddleware(RequestDelegate next)
{
    public Task InvokeAsync(HttpContext context)
    {
        var h = context.Response.Headers;
        h["X-Content-Type-Options"] = "nosniff";
        h["X-Frame-Options"] = "DENY";
        h["Referrer-Policy"] = "no-referrer";
        h["Cache-Control"] = "no-store";
        // CSP estricta excepto en la UI de documentación
        if (!context.Request.Path.StartsWithSegments("/scalar") && !context.Request.Path.StartsWithSegments("/swagger"))
            h["Content-Security-Policy"] = "default-src 'none'; frame-ancestors 'none'";
        return next(context);
    }
}
```

**`Extensions/RateLimitPolicies.cs`**

```csharp
namespace Erp.Api.Extensions;

public static class RateLimitPolicies
{
    public const string Auth = "auth";
    public const string PerUser = "per-user";
}
```

**`Extensions/PresentationServiceCollectionExtensions.cs`**

```csharp
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Threading.RateLimiting;
using Erp.Api.Contracts;
using Erp.Api.ErrorHandling;
using Erp.Api.OpenApi;
using Erp.Api.Services;
using Erp.Application.Abstractions.Security;
using Erp.Domain.Common;
using Erp.Infrastructure.Persistence;
using Erp.Infrastructure.Security;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.Tokens;

namespace Erp.Api.Extensions;

internal static class PresentationServiceCollectionExtensions
{
    public static IServiceCollection AddPresentation(this IServiceCollection services, IConfiguration config)
    {
        services.AddHttpContextAccessor();
        services.AddScoped<ICurrentUser, CurrentUser>();
        services.AddScoped<IRequestContext, HttpRequestContext>();

        services.AddControllers()
            .AddJsonOptions(o => o.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase)
            .ConfigureApiBehaviorOptions(o => o.InvalidModelStateResponseFactory = ctx =>
            {
                var errors = ctx.ModelState
                    .Where(kv => kv.Value?.Errors.Count > 0)
                    .SelectMany(kv => kv.Value!.Errors.Select(e => new FieldError(
                        JsonNamingPolicy.CamelCase.ConvertName(kv.Key.TrimStart('$', '.')),
                        "INVALID_FORMAT", "El valor enviado no tiene un formato válido.")))
                    .ToList();
                var traceId = Activity.Current?.Id ?? ctx.HttpContext.TraceIdentifier;
                return new BadRequestObjectResult(ApiResponse.Fail("BAD_REQUEST",
                    "La petición no tiene un formato válido.", traceId, errors));
            });

        services.AddExceptionHandler<GlobalExceptionHandler>();
        services.AddProblemDetails();

        // Autenticación JWT
        var jwt = config.GetSection(JwtOptions.SectionName).Get<JwtOptions>()
            ?? throw new InvalidOperationException("Falta la sección Jwt.");
        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(o =>
            {
                o.MapInboundClaims = false;
                o.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true, ValidIssuer = jwt.Issuer,
                    ValidateAudience = true, ValidAudience = jwt.Audience,
                    ValidateLifetime = true, ClockSkew = TimeSpan.FromSeconds(30),
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.SigningKey)),
                    ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
                    NameClaimType = "name",
                    RoleClaimType = "role"
                };
                o.Events = new JwtBearerEvents
                {
                    OnChallenge = AuthResponseWriter.WriteChallengeAsync,
                    OnForbidden = AuthResponseWriter.WriteForbiddenAsync
                };
            });
        services.AddAuthorization();

        // Rate limiting
        services.AddRateLimiter(o =>
        {
            o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            o.OnRejected = async (ctx, ct) =>
            {
                var traceId = Activity.Current?.Id ?? ctx.HttpContext.TraceIdentifier;
                await ctx.HttpContext.Response.WriteAsJsonAsync(ApiResponse.Fail("RATE_LIMITED",
                    "Demasiadas peticiones. Espera un momento e intenta de nuevo.", traceId), ct);
            };
            o.AddPolicy(RateLimitPolicies.Auth, http => RateLimitPartition.GetFixedWindowLimiter(
                http.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                _ => new FixedWindowRateLimiterOptions { PermitLimit = 10, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
            o.AddPolicy(RateLimitPolicies.PerUser, http => RateLimitPartition.GetTokenBucketLimiter(
                http.User.FindFirst("sub")?.Value ?? http.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                _ => new TokenBucketRateLimiterOptions
                {
                    TokenLimit = 100, TokensPerPeriod = 50, ReplenishmentPeriod = TimeSpan.FromSeconds(10), QueueLimit = 0
                }));
        });

        // Detrás de IIS / proxy / balanceador: IP real del cliente
        services.Configure<ForwardedHeadersOptions>(o =>
        {
            o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
            // PRODUCCIÓN: registrar aquí las IP del proxy en o.KnownProxies / o.KnownNetworks
        });

        services.AddHealthChecks().AddDbContextCheck<ErpDbContext>("sqlserver", tags: ["ready"]);
        services.AddApiDocumentation();
        return services;
    }
}
```

**`Program.cs`**

```csharp
using Erp.Api.Extensions;
using Erp.Api.Middleware;
using Erp.Api.OpenApi;
using Erp.Application;
using Erp.Infrastructure;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Serilog;

Log.Logger = new LoggerConfiguration().WriteTo.Console().CreateBootstrapLogger();

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
        .AddInfrastructure(builder.Configuration)
        .AddPresentation(builder.Configuration);

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

    app.Run();
}
catch (Exception ex) when (ex is not HostAbortedException)
{
    Log.Fatal(ex, "La aplicación terminó inesperadamente");
}
finally
{
    Log.CloseAndFlush();
}

public partial class Program; // para WebApplicationFactory en tests de integración
```

**`appsettings.json`** (sin secretos)

```json
{
  "ConnectionStrings": {
    "Administracion": ""
  },
  "Jwt": {
    "Issuer": "erp-api",
    "Audience": "erp-mobile",
    "SigningKey": "",
    "AccessTokenMinutes": 15
  },
  "Auth": {
    "MaxFailedAttempts": 5,
    "LockoutMinutes": 15,
    "RefreshTokenDays": 14,
    "AutoEnrollOnFirstLogin": true,
    "ActiveStatuses": [ "ACTIVO" ]
  },
  "OpenApi": {
    "Enabled": false
  },
  "Serilog": {
    "MinimumLevel": {
      "Default": "Information",
      "Override": {
        "Microsoft.AspNetCore": "Warning",
        "Microsoft.EntityFrameworkCore.Database.Command": "Warning"
      }
    },
    "WriteTo": [ { "Name": "Console" } ]
  },
  "AllowedHosts": "*"
}
```

**`appsettings.Development.json`**

```json
{
  "OpenApi": { "Enabled": true },
  "Serilog": { "MinimumLevel": { "Default": "Debug" } }
}
```

> `ConnectionStrings:Administracion` y `Jwt:SigningKey` vacíos a propósito: la app **no arranca** sin ellos (validación `ValidateOnStart`). Se configuran con user-secrets o variables de entorno (sección 13).

---

## 9. Flujo del login

```text
App móvil ── POST /api/v1/auth/login {userName, password, deviceId}
   │
   ▼
[Rate limit 10/min/IP] → [Model binding] → AuthController.Login
   │
   ▼
LoggingDecorator → ValidationDecorator (FluentValidation) → LoginCommandHandler
   │
   ├─ 1. SELECT dbo.Usuarios (Usuario, Contraseña, Nombre, TipoUsuario, Estatus)
   │      └─ no existe → verificación dummy + bitácora → 401 AUTH_INVALID_CREDENTIALS
   ├─ 2. seguridad.UsuariosApp (si no existe se crea: Enroll)
   ├─ 3. ¿Bloqueado? → 403 AUTH_ACCOUNT_LOCKED
   ├─ 4. Contraseña
   │      ├─ con PasswordHash → PBKDF2
   │      └─ sin PasswordHash → compara con dbo.Usuarios.Contraseña (tiempo constante)
   │                            └─ OK → guarda hash (migración) + bitácora PasswordMigrated
   │      └─ falla → IntentosFallidos++ (5 → bloqueo 15 min) → 401
   ├─ 5. ¿Estatus activo? → si no, 403 AUTH_USER_INACTIVE
   ├─ 6. ¿AccesoAppHabilitado? → si no, 403 AUTH_APP_ACCESS_DISABLED
   └─ 7. JWT (15 min) + refresh token (14 días, hash SHA-256) + bitácora → 200
```

---

## 10. Seguridad implementada en la fase 1

| Control | Implementación |
|---|---|
| Contraseñas | PBKDF2 (Identity V3) con sal aleatoria por usuario; migración progresiva desde texto plano |
| Enumeración de usuarios | Mismo mensaje y tiempo similar para usuario inexistente y contraseña incorrecta |
| Fuerza bruta | Bloqueo 15 min tras 5 fallos + rate limit 10/min por IP en `/auth/*` |
| Tokens | JWT HS256 15 min, llave ≥ 256 bits fuera del repositorio; `ValidAlgorithms` fijo; `ClockSkew` 30 s |
| Refresh tokens | Aleatorios de 64 bytes, guardados como SHA-256, rotación en cada uso, revocación por familia ante reuso |
| Autorización | `RequireAuthorization()` global; anónimos solo con `[AllowAnonymous]` explícito |
| Datos legados | Usuario SQL con `SELECT` solo en 5 columnas de `dbo.Usuarios`; nunca se escribe en esa tabla |
| Errores | `GlobalExceptionHandler` sin stack traces fuera de Development; `traceId` como folio |
| Transporte | HTTPS + HSTS; `Encrypt=True` hacia SQL Server |
| Headers | `nosniff`, `X-Frame-Options`, `Referrer-Policy`, `Cache-Control: no-store`, CSP, sin header `Server` |
| Auditoría | `seguridad.BitacoraAccesos` para login, fallos, bloqueos, migración de contraseña, refresh, reuso y logout |
| Logs | Nunca se registran contraseñas, tokens ni el contenido de los comandos |
| Documentación | OpenAPI/Scalar/Swagger solo si `OpenApi:Enabled = true` (Development/QA) |

---

## 11. Migraciones

```bash
# Instalar herramienta (una vez)
dotnet tool install --global dotnet-ef

# Crear la migración inicial (solo esquema seguridad; dbo.Usuarios está excluida)
dotnet ef migrations add InitialSecuritySchema \
  --project src/Erp.Infrastructure --startup-project src/Erp.Api \
  --output-dir Persistence/Migrations

# Revisar el SQL ANTES de aplicarlo (debe contener solo objetos del esquema seguridad)
dotnet ef migrations script --idempotent \
  --project src/Erp.Infrastructure --startup-project src/Erp.Api \
  --output database/InitialSecuritySchema.sql

# Desarrollo: aplicar con el usuario erp_migrator
dotnet ef database update --project src/Erp.Infrastructure --startup-project src/Erp.Api \
  --connection "Server=...;Database=Administracion;User Id=erp_migrator;Password=...;Encrypt=True;TrustServerCertificate=True"
```

**Reglas:**

- La API **no** ejecuta `Database.Migrate()` al arrancar.
- En QA y producción se aplica el script idempotente revisado, con respaldo previo de la BD.
- Verificar en el script que **no** aparezca `CREATE TABLE [dbo].[Usuarios]` ni ningún `ALTER` sobre `dbo.Usuarios`.

---

## 12. Tests requeridos

### 12.1 `Erp.Domain.UnitTests`

- `AppUser.RegisterFailedAttempt_FifthAttempt_LocksAccount`
- `AppUser.RegisterSuccessfulLogin_ResetsAttemptsAndLockout`
- `AppUser.IsLockedOut_AfterLockoutEnds_ReturnsFalse`
- `RefreshToken.Revoke_AlreadyRevoked_KeepsOriginalReason`
- `RefreshToken.IsActive_Expired_ReturnsFalse`

### 12.2 `Erp.Application.UnitTests` (EF InMemory o mocks + `FakeTimeProvider`)

- `Login_UnknownUser_ReturnsInvalidCredentials_AndCallsDummyVerify`
- `Login_FirstLoginCorrectLegacyPassword_MigratesHash_ReturnsTokens`
- `Login_FirstLoginWrongLegacyPassword_IncrementsAttempts`
- `Login_WithHash_IgnoresLegacyPassword`
- `Login_FiveFailures_ReturnsAccountLocked`
- `Login_InactiveStatus_ReturnsUserInactive`
- `Login_AppAccessDisabled_ReturnsAppAccessDisabled`
- `Login_EmptyUserName_ReturnsValidationError` (a través del decorador)
- `Refresh_ValidToken_RotatesAndRevokesPrevious`
- `Refresh_RevokedToken_RevokesWholeFamily`
- `Refresh_ExpiredToken_ReturnsInvalidRefreshToken`
- `Logout_RevokesFamily_IsIdempotent`

### 12.3 `Erp.Api.IntegrationTests` (WebApplicationFactory + Testcontainers.MsSql)

Antes de aplicar las migraciones, el fixture crea `dbo.Usuarios` con el DDL original (sección 2) e inserta usuarios de prueba (activo, inactivo, sin contraseña).

- `POST /login` correcto → 200 y sobre `ApiResponse` completo.
- `POST /login` incorrecto → 401 `AUTH_INVALID_CREDENTIALS`.
- `GET /me` sin token → 401 `UNAUTHORIZED` con sobre estándar.
- `GET /me` con token → 200.
- `POST /refresh` dos veces con el mismo token → la segunda 401 y la familia queda revocada.
- 11 logins seguidos desde la misma IP → 429 `RATE_LIMITED`.
- `GET /health/ready` → 200.
- `GET /openapi/v1.json` → 200 y contiene `/api/v1/auth/login`.

### 12.4 `Erp.ArchitectureTests` (NetArchTest)

- Domain no depende de `Microsoft.EntityFrameworkCore`, `Microsoft.AspNetCore` ni de otros proyectos.
- Application no depende de `Erp.Infrastructure` ni de `Erp.Api`.
- Infrastructure no depende de `Erp.Api`.
- Los handlers son `internal sealed`.

---

## 13. Comandos para crear y levantar el proyecto

```bash
mkdir Erp.Backend && cd Erp.Backend
dotnet new sln -n Erp.Backend
dotnet new gitignore
dotnet new editorconfig

dotnet new classlib -n Erp.Domain          -o src/Erp.Domain
dotnet new classlib -n Erp.Application     -o src/Erp.Application
dotnet new classlib -n Erp.Infrastructure  -o src/Erp.Infrastructure
dotnet new webapi   -n Erp.Api             -o src/Erp.Api --use-controllers

dotnet new xunit -n Erp.Domain.UnitTests       -o tests/Erp.Domain.UnitTests
dotnet new xunit -n Erp.Application.UnitTests  -o tests/Erp.Application.UnitTests
dotnet new xunit -n Erp.Api.IntegrationTests   -o tests/Erp.Api.IntegrationTests
dotnet new xunit -n Erp.ArchitectureTests      -o tests/Erp.ArchitectureTests

dotnet sln add src/*/*.csproj tests/*/*.csproj

dotnet add src/Erp.Application    reference src/Erp.Domain
dotnet add src/Erp.Infrastructure reference src/Erp.Application
dotnet add src/Erp.Api            reference src/Erp.Application src/Erp.Infrastructure
# (tests: referenciar los proyectos que prueban)

# Secretos de desarrollo (NUNCA en appsettings)
cd src/Erp.Api
dotnet user-secrets init
dotnet user-secrets set "ConnectionStrings:Administracion" "Server=localhost;Database=Administracion;User Id=erp_api;Password=***;Encrypt=True;TrustServerCertificate=True"
dotnet user-secrets set "Jwt:SigningKey" "$(openssl rand -base64 48)"
cd ../..

dotnet build
dotnet test
dotnet run --project src/Erp.Api
# Documentación: https://localhost:<puerto>/scalar/v1  y  https://localhost:<puerto>/swagger
```

En servidores, las mismas llaves se definen como variables de entorno: `ConnectionStrings__Administracion` y `Jwt__SigningKey`.

> Eliminar del template `webapi` el `WeatherForecastController` y el `WeatherForecast.cs`.

---

## 14. Criterios de aceptación (Definition of Done de la fase 1)

- [ ] `dotnet build` sin warnings; `dotnet test` en verde.
- [ ] La migración `InitialSecuritySchema` solo crea objetos del esquema `seguridad`.
- [ ] Un usuario activo de `dbo.Usuarios` puede hacer login con su contraseña actual; después del primer login, `seguridad.UsuariosApp.PasswordHash` contiene un hash (empieza con `AQAAAA`).
- [ ] Un usuario inactivo recibe 403 `AUTH_USER_INACTIVE`.
- [ ] Tras 5 intentos fallidos la cuenta queda bloqueada 15 minutos.
- [ ] Refresh rota tokens y el reuso de un token revoca la familia.
- [ ] Logout revoca la sesión y `/me` sigue funcionando hasta que expire el access token (comportamiento esperado de JWT).
- [ ] Toda respuesta (éxito, error de negocio, validación, 401 del middleware, 429, 500) usa el sobre `ApiResponse`.
- [ ] Scalar y Swagger muestran los 4 endpoints con descripciones, códigos de respuesta y el botón de autorización Bearer.
- [ ] Ningún log contiene contraseñas ni tokens.
- [ ] README con pasos de arranque, configuración de secretos y migraciones.

---

## 15. Siguientes fases (no generar ahora)

1. `POST /api/v1/auth/change-password` (requiere contraseña actual; revoca todas las sesiones).
2. Permisos por `TipoUsuario` o tabla `seguridad.Permisos` + políticas `[HasPermission]`.
3. Pantalla/endpoint administrativo para habilitar/deshabilitar acceso a la app y reiniciar contraseñas (`PasswordHash = NULL`).
4. Job de limpieza de refresh tokens expirados y bitácora antigua.
5. OpenTelemetry (trazas y métricas) y envío de logs a Seq/Elastic/Application Insights.
6. Módulos de negocio siguiendo la *Guía de Arquitectura e Implementación — Web API ERP*.
7. Proyecto aparte (recomendado): migrar el sistema de escritorio a contraseñas con hash.
