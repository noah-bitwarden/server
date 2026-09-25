using System.ComponentModel.DataAnnotations;
using Bit.Api.Models.Public.Response;

namespace Bit.Api.Billing.Public.Models;

/// <summary>
/// A provider invoice and all of its line items, including unassigned seats.
/// </summary>
public class ProviderInvoiceResponseModel : IResponseModel
{
    public ProviderInvoiceResponseModel(ProviderInvoiceGroup invoice)
    {
        ArgumentNullException.ThrowIfNull(invoice);

        InvoiceDate = DateTime.SpecifyKind(invoice.InvoiceDate, DateTimeKind.Utc);
        LineItems = invoice.Items
            .OrderBy(i => i.ClientId.HasValue ? 0 : 1)
            .ThenBy(i => i.ClientName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(i => i.PlanName, StringComparer.OrdinalIgnoreCase)
            .Select(i => new ProviderInvoiceLineItemResponseModel(i))
            .ToList();
    }

    /// <summary>
    /// String representing the object's type. Objects of the same type share the same properties.
    /// </summary>
    /// <example>providerInvoice</example>
    [Required]
    public string Object => "providerInvoice";
    /// <summary>
    /// The date and time (UTC) the invoice was created: the earliest time any of its line items was recorded.
    /// The client invoices endpoint returns the same value for this invoice.
    /// </summary>
    /// <example>2026-09-01T00:00:00Z</example>
    [Required]
    public DateTime InvoiceDate { get; }
    /// <summary>
    /// The invoice's line items. Client line items come first, followed by unassigned seats.
    /// </summary>
    [Required]
    public IEnumerable<ProviderInvoiceLineItemResponseModel> LineItems { get; }
}
