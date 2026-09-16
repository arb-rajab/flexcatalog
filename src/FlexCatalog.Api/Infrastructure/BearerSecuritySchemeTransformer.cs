using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace FlexCatalog.Api.Infrastructure;

/// <summary>Adds a JWT bearer security scheme to the generated OpenAPI document so Scalar's "Try it" can authenticate.</summary>
public sealed class BearerSecuritySchemeTransformer : IOpenApiDocumentTransformer
{
    public Task TransformAsync(OpenApiDocument document, OpenApiDocumentTransformerContext context, CancellationToken cancellationToken)
    {
        document.Components ??= new OpenApiComponents();
        document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();
        document.Components.SecuritySchemes["Bearer"] = new OpenApiSecurityScheme
        {
            Type = SecuritySchemeType.Http,
            Scheme = "bearer",
            BearerFormat = "JWT",
            Description = "Paste the token returned by POST /api/auth/login.",
        };

        return Task.CompletedTask;
    }
}
