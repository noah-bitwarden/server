namespace Bit.SharedWeb.Swagger;

/// <summary>
/// Documents a <see cref="string"/> property as an ISO 8601 calendar date (<c>yyyy-MM-dd</c>) in OpenAPI
/// (<c>format: date</c> with a matching <c>pattern</c>).
/// </summary>
/// <remarks>
/// Documentation only. Unlike <see cref="System.ComponentModel.DataAnnotations.RegularExpressionAttribute"/>, this
/// does not add model validation, so the endpoint can validate the value itself and return its own error.
/// </remarks>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Parameter)]
public sealed class IsoDateOnlyStringAttribute : Attribute
{
}
