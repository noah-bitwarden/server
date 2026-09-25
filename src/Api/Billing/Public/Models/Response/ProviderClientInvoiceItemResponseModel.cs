using System.ComponentModel.DataAnnotations;
using Bit.Api.Models.Public.Response;
using Bit.Core.Billing.Providers.Entities;

namespace Bit.Api.Billing.Public.Models;

/// <summary>
/// An invoice line item for a single client organization.
/// </summary>
public class ProviderClientInvoiceItemResponseModel : IResponseModel
{
    /// <param name="item">The client's line item.</param>
    /// <param name="invoiceDate">The invoice-level date of the invoice the line item belongs to.</param>
    public ProviderClientInvoiceItemResponseModel(ProviderInvoiceItem item, DateTime invoiceDate)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (!item.ClientId.HasValue)
        {
            throw new ArgumentException("Unassigned seat line items are not client line items.", nameof(item));
        }

        OrganizationId = item.ClientId.Value;
        ClientName = item.ClientName;
        PlanName = item.PlanName;
        AssignedSeats = item.AssignedSeats;
        UsedSeats = item.UsedSeats;
        RemainingSeats = item.AssignedSeats - item.UsedSeats;
        EstimatedTotal = UsdAmount.RoundToCents(item.Total);
        InvoiceDate = DateTime.SpecifyKind(invoiceDate, DateTimeKind.Utc);
    }

    /// <summary>
    /// String representing the object's type. Objects of the same type share the same properties.
    /// </summary>
    /// <example>providerClientInvoiceItem</example>
    [Required]
    public string Object => "providerClientInvoiceItem";
    /// <summary>
    /// The client organization's unique identifier.
    /// </summary>
    /// <example>3f2b1c4e-8a6d-4d2f-9b1e-5c7a0e9d4b21</example>
    [Required]
    public Guid OrganizationId { get; }
    /// <summary>
    /// The client organization's name at the time the invoice was created.
    /// </summary>
    /// <example>Acme Corp</example>
    [Required]
    public string ClientName { get; }
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
    /// <summary>
    /// The date and time (UTC) the invoice was created: the earliest time any of the invoice's line items was recorded.
    /// It is the same value the provider-level invoices endpoint returns for this invoice, so it can be used to
    /// match the two.
    /// </summary>
    /// <example>2026-09-01T00:00:00Z</example>
    [Required]
    public DateTime InvoiceDate { get; }
}
