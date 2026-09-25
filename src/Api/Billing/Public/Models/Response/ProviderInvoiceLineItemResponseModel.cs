using System.ComponentModel.DataAnnotations;
using Bit.Api.Models.Public.Response;
using Bit.Core.Billing.Providers.Entities;

namespace Bit.Api.Billing.Public.Models;

/// <summary>
/// A single line item on a provider invoice. Either a client organization's seats or the provider's unassigned seats.
/// </summary>
public class ProviderInvoiceLineItemResponseModel : IResponseModel
{
    public const string ClientType = "client";
    public const string UnassignedType = "unassigned";

    public ProviderInvoiceLineItemResponseModel(ProviderInvoiceItem item)
    {
        ArgumentNullException.ThrowIfNull(item);

        var isClient = item.ClientId.HasValue;
        Type = isClient ? ClientType : UnassignedType;
        OrganizationId = item.ClientId;
        ClientName = isClient ? item.ClientName : null;
        PlanName = item.PlanName;
        AssignedSeats = item.AssignedSeats;
        UsedSeats = item.UsedSeats;
        RemainingSeats = item.AssignedSeats - item.UsedSeats;
        EstimatedTotal = UsdAmount.RoundToCents(item.Total);
    }

    /// <summary>
    /// String representing the object's type. Objects of the same type share the same properties.
    /// </summary>
    /// <example>providerInvoiceLineItem</example>
    [Required]
    public string Object => "providerInvoiceLineItem";
    /// <summary>
    /// The kind of line item: <c>client</c> for a client organization, or <c>unassigned</c> for seats
    /// the provider is billed for that are not assigned to any client.
    /// </summary>
    /// <example>client</example>
    [Required]
    public string Type { get; }
    /// <summary>
    /// The client organization's unique identifier. Null for unassigned seats.
    /// </summary>
    /// <example>3f2b1c4e-8a6d-4d2f-9b1e-5c7a0e9d4b21</example>
    public Guid? OrganizationId { get; }
    /// <summary>
    /// The client organization's name at the time the invoice was created. Null for unassigned seats.
    /// </summary>
    /// <example>Acme Corp</example>
    public string? ClientName { get; }
    /// <summary>
    /// The display name of the plan.
    /// </summary>
    /// <example>Enterprise (Monthly)</example>
    [Required]
    public string PlanName { get; }
    /// <summary>
    /// The number of seats assigned.
    /// </summary>
    /// <example>25</example>
    [Required]
    public int AssignedSeats { get; }
    /// <summary>
    /// The number of seats in use.
    /// </summary>
    /// <example>22</example>
    [Required]
    public int UsedSeats { get; }
    /// <summary>
    /// Assigned seats minus used seats.
    /// </summary>
    /// <example>3</example>
    [Required]
    public int RemainingSeats { get; }
    /// <summary>
    /// The estimated total for the line item in USD, rounded to two decimal places, calculated when the invoice
    /// was created. It may differ from the final invoice (for example, due to tax, credits, or proration).
    /// </summary>
    /// <example>75.00</example>
    [Required]
    public decimal EstimatedTotal { get; }
}
