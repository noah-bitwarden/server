namespace Bit.Core.Dirt.Events.ProviderClientEvents;

public record ProviderClientEventsRequest
{
    /// <summary>
    /// The authenticated provider. Client organizations are always derived from this provider.
    /// </summary>
    public required Guid ProviderId { get; init; }

    /// <summary>
    /// The start of the date window. Ignored when <see cref="ContinuationToken"/> is present.
    /// </summary>
    public required DateTime Start { get; init; }

    /// <summary>
    /// The end of the date window, clamped to the current time. Ignored when <see cref="ContinuationToken"/> is present.
    /// </summary>
    public required DateTime End { get; init; }

    /// <summary>
    /// Restricts results to these client organizations. Every ID must be linked to the provider.
    /// </summary>
    public IReadOnlyCollection<Guid>? OrganizationIds { get; init; }

    /// <summary>
    /// The cursor returned by the previous page.
    /// </summary>
    public string? ContinuationToken { get; init; }
}
