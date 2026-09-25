using System.Globalization;
using Bit.Core.Exceptions;
using Bit.SharedWeb.Swagger;

namespace Bit.Api.Billing.Public.Models;

public class ProviderInvoiceFilterRequestModel
{
    private const string DateFormat = "yyyy-MM-dd";

    // Start and End are strings so default DateTime model binding (culture-dependent, lenient) never runs.
    // They are validated in ToDateRange rather than with validation attributes so every date problem returns
    // the same error shape.

    /// <summary>
    /// The first day to include, as an ISO 8601 date (<c>yyyy-MM-dd</c>, UTC calendar day). Inclusive.
    /// If both start and end are omitted, only the most recent invoice is returned.
    /// </summary>
    /// <example>2026-09-01</example>
    [IsoDateOnlyString]
    public string? Start { get; set; }

    /// <summary>
    /// The last day to include, as an ISO 8601 date (<c>yyyy-MM-dd</c>, UTC calendar day). Inclusive.
    /// If both start and end are omitted, only the most recent invoice is returned.
    /// </summary>
    /// <example>2026-09-30</example>
    [IsoDateOnlyString]
    public string? End { get; set; }

    /// <summary>
    /// Parses and validates the date range.
    /// </summary>
    /// <exception cref="BadRequestException">A date is not a valid <c>yyyy-MM-dd</c> date, or start is after end.</exception>
    public ProviderInvoiceDateRange ToDateRange()
    {
        var start = Parse(Start, "start");
        var end = Parse(End, "end");

        if (start > end)
        {
            throw new BadRequestException("The start date must not be after the end date.");
        }

        return new ProviderInvoiceDateRange(start, end);
    }

    private static DateOnly? Parse(string? value, string name)
    {
        if (string.IsNullOrEmpty(value))
        {
            return null;
        }

        // The length check rejects forms like 2026-9-1 that a lenient parser might accept.
        if (value.Length != DateFormat.Length ||
            !DateOnly.TryParseExact(value, DateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
        {
            throw new BadRequestException($"The {name} date must be a valid date in yyyy-MM-dd format.");
        }

        return date;
    }
}
