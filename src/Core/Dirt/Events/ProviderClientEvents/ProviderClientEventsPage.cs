using Bit.Core.Models.Data;

namespace Bit.Core.Dirt.Events.ProviderClientEvents;

/// <summary>
/// One page of client organization events. A null <see cref="ContinuationToken"/> means there are no more pages;
/// a page can be short, or empty, while the token is non-null.
/// </summary>
public record ProviderClientEventsPage(IReadOnlyList<IEvent> Data, string? ContinuationToken)
{
    public static ProviderClientEventsPage Empty { get; } = new([], null);
}
