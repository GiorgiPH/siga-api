using System.Reflection;
using NetArchTest.Rules;
using Siga.Application.Abstractions.Messaging;

namespace Siga.ArchitectureTests;

public sealed class LayerDependencyTests
{
    private const string DomainNs = "Siga.Domain";
    private const string ApplicationNs = "Siga.Application";
    private const string InfrastructureNs = "Siga.Infrastructure";
    private const string ApiNs = "Siga.Api";

    private static readonly Assembly DomainAssembly = typeof(Siga.Domain.Common.Result).Assembly;
    private static readonly Assembly ApplicationAssembly = typeof(Siga.Application.DependencyInjection).Assembly;
    private static readonly Assembly InfrastructureAssembly = typeof(Siga.Infrastructure.DependencyInjection).Assembly;

    [Fact]
    public void Domain_ShouldNotDependOn_EfCoreAspNetCoreOrOtherLayers()
    {
        var result = Types.InAssembly(DomainAssembly)
            .ShouldNot()
            .HaveDependencyOnAny("Microsoft.EntityFrameworkCore", "Microsoft.AspNetCore",
                ApplicationNs, InfrastructureNs, ApiNs)
            .GetResult();

        result.IsSuccessful.ShouldBeTrue(Describe(result));
    }

    [Fact]
    public void Application_ShouldNotDependOn_InfrastructureOrApi()
    {
        var result = Types.InAssembly(ApplicationAssembly)
            .ShouldNot()
            .HaveDependencyOnAny(InfrastructureNs, ApiNs)
            .GetResult();

        result.IsSuccessful.ShouldBeTrue(Describe(result));
    }

    [Fact]
    public void Infrastructure_ShouldNotDependOn_Api()
    {
        var result = Types.InAssembly(InfrastructureAssembly)
            .ShouldNot()
            .HaveDependencyOn(ApiNs)
            .GetResult();

        result.IsSuccessful.ShouldBeTrue(Describe(result));
    }

    [Fact]
    public void Handlers_ShouldBeInternalSealed()
    {
        Type[] handlerInterfaces = [typeof(ICommandHandler<,>), typeof(IQueryHandler<,>)];

        var handlers = ApplicationAssembly.GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false })
            .Where(t => !t.Name.Contains("Decorator", StringComparison.Ordinal))
            .Where(t => t.GetInterfaces().Any(i => i.IsGenericType && handlerInterfaces.Contains(i.GetGenericTypeDefinition())))
            .ToList();

        handlers.ShouldNotBeEmpty();
        handlers.Where(t => t.IsPublic || !t.IsSealed).Select(t => t.FullName).ShouldBeEmpty();
    }

    [Fact]
    public void Domain_ShouldNotReference_DomainNamespaceOutsideItself()
    {
        Types.InAssembly(DomainAssembly).GetTypes()
            .ShouldAllBe(t => t.Namespace != null && t.Namespace.StartsWith(DomainNs, StringComparison.Ordinal));
    }

    private static string Describe(TestResult result) =>
        result.IsSuccessful ? string.Empty : "Tipos que violan la regla: " + string.Join(", ", result.FailingTypeNames ?? []);
}
