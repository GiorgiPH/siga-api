# ADR-004 — Sin MediatR ni AutoMapper

**Estado:** Aceptado

## Contexto
MediatR y AutoMapper pasaron a licencia comercial en 2025.

## Decisión
- Interfaces propias `ICommand<T>`, `IQuery<T>`, `ICommandHandler<,>`, `IQueryHandler<,>`.
- Decoradores registrados con **Scrutor**: `LoggingCommandHandlerDecorator` → `ValidationCommandHandlerDecorator` → handler.
- Validación con **FluentValidation**.
- Mapeo manual (`ToCommand()`, constructores de `*Response`).

## Consecuencias
Mismo nivel de desacoplamiento, sin costo de licencia y sin "magia" difícil de depurar. Los controllers reciben el handler por `[FromServices]`.

> Por la misma razón, los tests usan **Shouldly** en lugar de FluentAssertions (v8 también es comercial).
