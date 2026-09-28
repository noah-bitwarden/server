using System.Reflection;
using Microsoft.OpenApi;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace Bit.SharedWeb.Swagger;

/// <summary>
/// Sets <c>format: date</c> and <c>pattern: ^\d{4}-\d{2}-\d{2}$</c> on string schemas marked with
/// <see cref="IsoDateOnlyStringAttribute"/>.
/// </summary>
public class IsoDateOnlyStringSchemaFilter : ISchemaFilter
{
    public const string Pattern = @"^\d{4}-\d{2}-\d{2}$";

    public void Apply(IOpenApiSchema schema, SchemaFilterContext context)
    {
        var marked = context.MemberInfo?.GetCustomAttribute<IsoDateOnlyStringAttribute>() != null ||
                     context.ParameterInfo?.GetCustomAttribute<IsoDateOnlyStringAttribute>() != null;
        if (!marked || schema is not OpenApiSchema openApiSchema)
        {
            return;
        }

        openApiSchema.Format = "date";
        openApiSchema.Pattern = Pattern;
    }
}
