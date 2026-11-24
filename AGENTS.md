# AGENTS.md — SIGA Web API

Web API (.NET 10) para la app móvil (.NET MAUI) de **SIGA**. SQL Server, BD `Administracion` (compartida con el sistema de escritorio).
Código en **inglés**; BD, mensajes al usuario y docs en **español**. Detalle: [README.md](README.md), [docs/adr/](docs/adr/).
Si una petición choca con estas reglas, pregunta antes de romperlas. Si cambias una regla, actualiza este archivo.

## 1. Stack
| Área | Tecnología |
|---|---|
| Web | ASP.NET Core **Controllers** (.NET 10, C# latest, `Nullable`, `TreatWarningsAsErrors`) |
| Datos | EF Core 10 + SQL Server; migraciones en `Siga.Infrastructure` |
| Casos de uso | `ICommand/IQuery` + handlers propios, decoradores con **Scrutor**, **FluentValidation** |
| Auth | JWT HS256 (15 min) + refresh token rotativo (14 días), PBKDF2 |
| Otros | Serilog, OpenAPI + Scalar (`/scalar/v1`) + Swagger (`/swagger`), health checks, rate limiting |
| Tests | xUnit, Shouldly, NSubstitute, EF InMemory, `FakeTimeProvider`, Testcontainers.MsSql, NetArchTest |

**Prohibido:** MediatR, AutoMapper, FluentAssertions (licencia comercial). Versiones solo en `Directory.Packages.props` (nunca `Version=` en `.csproj`). Paquete nuevo = licencia MIT/Apache/BSD y estable para .NET 10.

## 2. Arquitectura (Clean Architecture)
`Api → Application → Domain` · `Infrastructure → Application` · Api referencia Infrastructure solo para `AddInfrastructure()`. Lo verifican `Siga.ArchitectureTests` (no desactivarlos).

| Capa | SÍ | NO |
|---|---|---|
| **Domain** | Entidades con setters `private` y fábricas (`Create`, `Issue`); comportamiento (`Revoke`); `Result`/`Error`; `{Tema}Errors` | EF, JSON, HTTP, I/O, logging, `DateTime.Now` |
| **Application** | Command/Query + Handler + Validator por caso de uso; `*Response`; `*Options`; interfaces que implementa Infrastructure | `HttpContext`, `IActionResult`, SQL crudo, `SigaDbContext` concreto, excepciones para errores esperados |
| **Infrastructure** | `SigaDbContext`, `{Entidad}Configuration`, migraciones, JWT, hash, servicios externos | Lógica de negocio, validaciones, conocer `ApiResponse` |
| **Api** | Controllers delgados, `*Request`, middleware, auth, rate limit, OpenAPI, health | Lógica, `DbContext`, LINQ, `try/catch`, devolver entidades |

## 3. Estructura
```
src/Siga.Domain/          Common/ · {Modulo}/ (entidades, {Tema}Errors)
src/Siga.Application/     Abstractions/ · Behaviors/ · Features/{Modulo}/{CasoDeUso}/ · DependencyInjection.cs
src/Siga.Infrastructure/  Persistence/{SigaDbContext, Configurations/, Migrations/} · Security/ · DependencyInjection.cs
src/Siga.Api/             Contracts/{Modulo}/ · Controllers/V1/ · ErrorHandling/ · Middleware/ · OpenApi/ · Services/ · Extensions/
tests/                    Siga.Domain.UnitTests · Siga.Application.UnitTests · Siga.Api.IntegrationTests · Siga.ArchitectureTests
database/                 NNN_descripcion.sql (permisos, scripts revisados) · docs/adr/ADR-NNN-titulo.md
```

## 4. Nomenclatura
| Elemento | Patrón |
|---|---|
| Comando / Query | `{Verbo}{Entidad}Command` · `Get{Algo}Query` / `List{Algo}Query` |
| Handler / Validador | `{Comando}Handler` · `{Comando}Validator` — **`internal sealed`** |
| DTO entrada / salida | `{Accion}{Entidad}Request` (Api/Contracts) · `{Entidad}Response` (Application) |
| Controller | `{Modulo}Controller` en `Controllers/V{n}` |
| EF / Opciones / Errores | `{Entidad}Configuration` · `{Tema}Options` + `SectionName` · `static class {Tema}Errors` |
| Tests | `Metodo_Escenario_ResultadoEsperado` |

- Un tipo principal por archivo, mismo nombre. Namespaces file-scoped = carpeta. Clases `sealed`, `internal` si no deben ser públicas.
- Primary constructors para DI; campos `_camelCase`; async con `CancellationToken ct`; `record` para DTOs/comandos.
- **BD:** esquema por módulo (`seguridad`, `ventas`…; nunca objetos nuevos en `dbo`); tablas/columnas en español PascalCase mapeadas con `HasColumnName`; `PK_{Tabla}`, `FK_{Tabla}_{Ref}`, `IX_`/`UX_{Tabla}_{Cols}`; fechas `datetimeoffset` con sufijo `Utc`; `HasMaxLength` en todo string; `rowversion` para concurrencia.
- **Rutas:** `api/v{n}/{recurso-kebab-plural}`; acciones no-CRUD como sub-recurso (`POST api/v1/orders/{id}/cancel`). JSON en camelCase.

## 5. Patrón de un caso de uso
Flujo: Controller → `LoggingDecorator` → `ValidationDecorator` → Handler → `HandleResult`.
Referencia viva: `Features/Auth/Login/` y `Controllers/V1/AuthController.cs`. Copia ese patrón, no inventes otro.

Pasos para agregar uno:
1. **Domain:** entidad con fábrica y métodos de comportamiento; errores en `{Tema}Errors`.
2. **Application:** carpeta `Features/{Modulo}/{CasoDeUso}/` con `record` Command/Query (`ICommand<TResponse>`), Validator (`WithErrorCode` + mensaje en español) y Handler que devuelve `Result<TResponse>`: errores esperados → `{Tema}Errors`; éxito → `*Response` con mapeo manual.
3. **Infrastructure:** `DbSet` + `{Entidad}Configuration` + migración (si hay entidad nueva).
4. **Api:** `*Request` con `ToCommand()`; acción del controller que recibe el handler por `[FromServices]`, llama `HandleResult(...)`, con comentarios XML y un `ProducesResponseType` por cada status posible.
5. **Tests:** unitarios del handler y de integración del endpoint.

Handlers y validadores se registran solos (Scrutor / `AddValidatorsFromAssembly`). Si una query recibe parámetros, crear y registrar un `ValidationQueryHandlerDecorator`.

## 6. DTOs
- Request (`ToCommand()`, normaliza `null`) → Command/Query → Response. Entidades **nunca** se reciben ni se devuelven.
- Nunca exponer `IQueryable`, hashes, contraseñas, tokens, `image`/binarios.
- Lecturas: `AsNoTracking()` + `.Select(x => new XResponse(...))`. Listados paginados (`page`, `pageSize` ≤ 100).
- Documentar Request/Response con XML (`<summary>`, `<param>`): se publican en OpenAPI.

## 7. Respuesta y status codes (contrato con la app — no cambiar sin versionar)
Sobre `ApiResponse<T>` (en `Api/Contracts/ApiResponse.cs`): `success`, `code`, `message`, `data`, `errors[] { field, code, message }`, `traceId`, `timestamp`.
- Controllers: **siempre** `HandleResult(result)`; nunca `Ok()`/`BadRequest()`/`NotFound()` directos. Sin datos → `Success.Instance` (`data: {}`).
- `ErrorType` → HTTP: **Validation 422 · Unauthorized 401 · Forbidden 403 · NotFound 404 · Conflict 409 · Failure 400**.
- Errores esperados → `Result` + `{Tema}Errors`. `code` en `SCREAMING_SNAKE_CASE` con prefijo de módulo (`AUTH_`, `CUSTOMER_`); `message` en español, mostrable al usuario. Un `code` publicado no se renombra.
- `errors[].code`: `REQUIRED`, `MAX_LENGTH`, `MIN_LENGTH`, `INVALID_FORMAT`, `OUT_OF_RANGE`.
- `traceId` = folio de soporte. `detail` (stack trace) solo en Development.

**Manejo global forzoso (`GlobalExceptionHandler`)** — excepciones solo para lo inesperado:

| Excepción | HTTP / code |
|---|---|
| `DbUpdateConcurrencyException`, `DbUpdateException` | 409 `CONFLICT` |
| `BadHttpRequestException` | 400 `BAD_REQUEST` |
| `OperationCanceledException` | 499 `REQUEST_CANCELLED` |
| cualquier otra | 500 `SERVER_ERROR` |

También usan el sobre: JSON inválido (400 `BAD_REQUEST`), JWT (401 `UNAUTHORIZED` / `TOKEN_EXPIRED` + `X-Token-Expired`, 403 `FORBIDDEN`) y rate limit (429 `RATE_LIMITED`). Nueva excepción esperable → agregarla al switch del handler global, nunca `try/catch` en controllers.

## 8. Versionamiento
- `[Route("api/v1/...")]` en `Controllers/V1/`. Cambio compatible (campo opcional, endpoint nuevo) → misma versión.
- Incompatible (renombrar/quitar campo, cambiar tipo o `code`) → `Controllers/V2/` + documento OpenAPI `v2`; `V1` se mantiene hasta que la app migre. Los handlers se reutilizan.

## 9. Seguridad (implementada — no degradar)
| Control | Regla |
|---|---|
| Autorización | Global (`RequireAuthorization`); anónimo solo con `[AllowAnonymous]` justificado + `[EnableRateLimiting(RateLimitPolicies.Auth)]` |
| Identidad | Usuario actual solo vía `ICurrentUser` (claim `sub`); permisos futuros por políticas, nunca `if (role == ...)` |
| Contraseñas | PBKDF2; texto plano solo en `LegacyPasswordVerifier` (primer login); mismo mensaje para usuario inexistente/clave mala |
| Fuerza bruta | 5 intentos → bloqueo 15 min; rate limit `auth` 10/min/IP; `PerUser` para endpoints costosos |
| Tokens | Refresh de 64 bytes guardado como SHA-256, rotación y revocación por familia; nunca tokens en claro |
| Secretos | Solo user-secrets / variables de entorno; toda `*Options` con `ValidateDataAnnotations().ValidateOnStart()` |
| BD | Usuario `siga_api` con mínimo privilegio; nunca escribir en `dbo.*`; esquema nuevo = `GRANT` en `database/NNN_*.sql` |
| Inyección | Solo LINQ/EF parametrizado; prohibido concatenar SQL |
| Transporte/headers | HTTPS + HSTS, `Encrypt=True`, `SecurityHeadersMiddleware`, sin header `Server`; en prod configurar `KnownProxies` |
| Logs / auditoría | Nunca contraseñas, tokens ni payloads; operaciones sensibles en `seguridad.BitacoraAccesos` |
| Docs | OpenAPI/Scalar/Swagger solo con `OpenApi:Enabled=true` (no en producción) |

## 10. Persistencia, tiempo y logging
- Nuevo `DbSet` en `IApplicationDbContext` y `SigaDbContext` + su `{Entidad}Configuration`. Tablas legadas: `ExcludeFromMigrations()` y solo columnas necesarias.
- Un `SaveChangesAsync(ct)` por caso de uso. Prohibido `.Result`, `.Wait()`, `async void`, N+1, `ToList()` antes de filtrar.
- Fechas solo con `TimeProvider` (`clock.GetUtcNow()`). Opciones leídas con `IOptions<T>`, no en el registro de servicios.
- Logging con `[LoggerMessage]` y plantillas estructuradas.
- Migraciones (`dotnet tool restore` primero):
```bash
dotnet ef migrations add <Nombre> --project src/Siga.Infrastructure --startup-project src/Siga.Infrastructure --output-dir Persistence/Migrations
dotnet ef migrations script --idempotent --project src/Siga.Infrastructure --startup-project src/Siga.Infrastructure --output database/<Nombre>.sql
```
Revisar el script (sin `CREATE/ALTER` en `dbo`). Nunca editar migraciones aplicadas. La API no ejecuta `Migrate()` al arrancar.

## 11. Pruebas
| Proyecto | Cubre |
|---|---|
| Domain.UnitTests | Métodos y reglas de entidades |
| Application.UnitTests | Handlers y validadores (`TestDbContext`, `AuthTestContext`, NSubstitute, `FakeTimeProvider`) |
| Api.IntegrationTests | Endpoints reales, sobre y status codes (`SigaApiFactory` + `SqlServerFixture`, requiere Docker) |
| ArchitectureTests | Dependencias entre capas, handlers `internal sealed` |

Cada caso de uso: camino feliz + cada error + validación. Cada endpoint: test de integración del 200 y de un error. Sin `Thread.Sleep`.

## 12. Terminado =
1. `dotnet build Siga.Backend.slnx` → 0 warnings, 0 errores.
2. `dotnet test Siga.Backend.slnx` → todo en verde.
3. Endpoint visible en `/scalar/v1` con XML y `ProducesResponseType`.
4. Sin secretos en código/logs; migración + script revisado si cambió el modelo.
5. Supresiones de analizador puntuales y justificadas; desviaciones marcadas con `// AJUSTE: motivo`.

## 13. Prohibido (resumen)
❌ Lógica, `DbContext` o `try/catch` en controllers · ❌ Respuestas fuera de `ApiResponse<T>` · ❌ Excepciones para errores esperados
❌ MediatR / AutoMapper / FluentAssertions · ❌ `DateTime.Now`, `.Result`, `.Wait()`, `async void`, `catch` vacío
❌ Escribir en `dbo` o crear objetos ahí · ❌ Editar migraciones aplicadas · ❌ Secretos en el repo
❌ Endpoints sin versión · ❌ Romper el contrato de la app sin nueva versión

## 14. Comandos
```bash
dotnet build Siga.Backend.slnx
dotnet test Siga.Backend.slnx
dotnet run --project src/Siga.Api --launch-profile https   # https://localhost:7027/scalar/v1
cd src/Siga.Api && dotnet user-secrets set "ConnectionStrings:Administracion" "..."
cd src/Siga.Api && dotnet user-secrets set "Jwt:SigningKey" "<32+ caracteres>"
```
