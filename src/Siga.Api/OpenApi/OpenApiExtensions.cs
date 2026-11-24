using Scalar.AspNetCore;

namespace Siga.Api.OpenApi;

internal static class OpenApiExtensions
{
    public static IServiceCollection AddApiDocumentation(this IServiceCollection services)
    {
        services.AddOpenApi("v1", options =>
        {
            options.AddDocumentTransformer((doc, _, _) =>
            {
                doc.Info.Title = "SIGA Administración — API";
                doc.Info.Version = "v1";
                doc.Info.Description = "API para la aplicación móvil de SIGA. Todas las respuestas usan el sobre ApiResponse<T>.";
                return Task.CompletedTask;
            });
            options.AddDocumentTransformer<BearerSecuritySchemeTransformer>();
        });
        return services;
    }

    public static WebApplication UseApiDocumentation(this WebApplication app)
    {
        if (!app.Configuration.GetValue<bool>("OpenApi:Enabled")) return app;

        app.MapOpenApi().AllowAnonymous();                                        // /openapi/v1.json
        app.MapScalarApiReference(o => o.WithTitle("SIGA API")).AllowAnonymous(); // /scalar/v1
        app.UseSwaggerUI(o =>                                                     // /swagger
        {
            o.SwaggerEndpoint("/openapi/v1.json", "SIGA API v1");
            o.DocumentTitle = "SIGA API";
        });
        return app;
    }
}
