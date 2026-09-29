using Bit.Api.Utilities;
using Bit.Core.Dirt.Events.ProviderClientEvents;

namespace Bit.Api.Dirt.Public.Models;

/// <summary>
/// Filters a provider's client organization event logs.
/// </summary>
public class ProviderEventFilterRequestModel
{
    /// <summary>
    /// The start date. If omitted, defaults to 30 days before the end date (or 30 days ago when no end date is given).
    /// Ignored when <c>continuationToken</c> is provided.
    /// </summary>
    public DateTime? Start { get; set; }

    /// <summary>
    /// The end date. If omitted, defaults to the current time. Ignored when <c>continuationToken</c> is provided.
    /// </summary>
    public DateTime? End { get; set; }

    /// <summary>
    /// A cursor for use in pagination. The date range is carried in the cursor, so every page covers the same range.
    /// </summary>
    public string? ContinuationToken { get; set; }

    /// <summary>
    /// Builds the query request. Dates are resolved the same way as the organization events API; with a
    /// continuation token they are ignored, because the query takes the date range from the token.
    /// </summary>
    public ProviderClientEventsRequest ToQueryRequest(Guid providerId)
    {
        var (start, end) = string.IsNullOrEmpty(ContinuationToken)
            ? ApiHelpers.GetDateRange(Start, End)
            : new Tuple<DateTime, DateTime>(default, default);

        return new ProviderClientEventsRequest
        {
            ProviderId = providerId,
            Start = start,
            End = end,
            ContinuationToken = ContinuationToken,
        };
    }
}
