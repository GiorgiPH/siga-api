using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using Siga.Application.Abstractions.Messaging;
using Siga.Application.Behaviors;
using Siga.Application.Features.Auth;

namespace Siga.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        var assembly = typeof(DependencyInjection).Assembly;

        services.Scan(scan => scan.FromAssemblies(assembly)
            .AddClasses(c => c.AssignableTo(typeof(ICommandHandler<,>)).Where(t => !t.Name.Contains("Decorator", StringComparison.Ordinal)), publicOnly: false)
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
