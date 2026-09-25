using Bit.Core.Billing.Providers.Entities;

namespace Bit.Api.Billing.Public.Models;

/// <summary>
/// An inclusive range of UTC calendar days. Either bound may be omitted.
/// </summary>
public readonly record struct ProviderInvoiceDateRange(DateOnly? Start, DateOnly? End)
{
    /// <summary>
    /// Whether neither a start nor an end date was provided.
    /// </summary>
    public bool IsEmpty => !Start.HasValue && !End.HasValue;

    /// <summary>
    /// Whether <paramref name="invoiceDate"/> (UTC) falls on or after <see cref="Start"/> 00:00:00Z and before
    /// the day after <see cref="End"/> 00:00:00Z.
    /// </summary>
    public bool Contains(DateTime invoiceDate)
    {
        if (Start.HasValue && invoiceDate < Start.Value.ToDateTime(TimeOnly.MinValue))
        {
            return false;
        }

        // DateOnly.MaxValue has no next day, so it has no upper bound.
        if (End.HasValue && End.Value != DateOnly.MaxValue &&
            invoiceDate >= End.Value.AddDays(1).ToDateTime(TimeOnly.MinValue))
        {
            return false;
        }

        return true;
    }
}

/// <summary>
/// All of a provider's line items for one invoice.
/// </summary>
/// <param name="InvoiceDate">
/// The earliest <see cref="ProviderInvoiceItem.Created"/> across all of the invoice's line items (UTC).
/// Both public endpoints use this same date for an invoice, so it can be used to match them.
/// </param>
/// <param name="Items">The invoice's line items.</param>
public sealed record ProviderInvoiceGroup(DateTime InvoiceDate, IReadOnlyList<ProviderInvoiceItem> Items);

/// <summary>
/// Grouping, dating, and date-range selection shared by both provider invoice endpoints, so they cannot drift apart.
/// </summary>
public static class ProviderInvoiceGroups
{
    /// <summary>
    /// Groups line items by invoice, newest first. Pass all of the provider's line items (not just one client's)
    /// so each invoice is dated from all of its rows.
    /// </summary>
    public static IReadOnlyList<ProviderInvoiceGroup> Build(IEnumerable<ProviderInvoiceItem> items) =>
        items
            // The Stripe invoice ID is only used for grouping and is never exposed.
            .GroupBy(i => i.InvoiceId)
            .Select(g => new ProviderInvoiceGroup(
                DateTime.SpecifyKind(g.Min(i => i.Created), DateTimeKind.Utc),
                g.ToList()))
            .OrderByDescending(g => g.InvoiceDate)
            .ToList();

    /// <summary>
    /// Selects the invoices whose <see cref="ProviderInvoiceGroup.InvoiceDate"/> is in <paramref name="range"/>.
    /// An invoice is either entirely in or entirely out. If the range is empty, selects only the latest invoice.
    /// </summary>
    /// <param name="groups">Invoices ordered newest first, as returned by <see cref="Build"/>.</param>
    /// <param name="range">The date range.</param>
    public static IEnumerable<ProviderInvoiceGroup> Select(
        IEnumerable<ProviderInvoiceGroup> groups, ProviderInvoiceDateRange range) =>
        range.IsEmpty
            ? groups.Take(1)
            : groups.Where(g => range.Contains(g.InvoiceDate));
}
