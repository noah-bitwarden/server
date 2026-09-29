using System.Text.Json.Serialization;

namespace Bit.Core.Dirt.Events.ProviderClientEvents;

internal enum ProviderClientEventsMode
{
    OrgMajor,
    SingleOrg,
    Merged,
}

/// <summary>
/// The payload of a provider client events continuation token. Inner tokens are backend-specific
/// (an Azure SDK resume string in the cloud, a binary date on SQL/EF) and are passed through unparsed.
/// </summary>
internal sealed record ProviderClientEventsCursor
{
    [JsonPropertyName("v")]
    public int Version { get; init; } = ProviderClientEventsCursorProtector.CurrentVersion;
    public required ProviderClientEventsMode Mode { get; init; }
    public required Guid ProviderId { get; init; }
    public required DateTime Start { get; init; }
    public required DateTime End { get; init; }
    public required string FilterHash { get; init; }

    /// <summary>Org-major: the organization being read, or the last one finished when <see cref="InnerToken"/> is null.</summary>
    public Guid? LastOrgId { get; init; }

    /// <summary>Single-org: the organization the token was issued for.</summary>
    public Guid? OrgId { get; init; }

    /// <summary>Org-major and single-org: the repository continuation token.</summary>
    public string? InnerToken { get; init; }

    /// <summary>Merged: the read position in each organization.</summary>
    public List<ProviderClientEventsPosition>? Positions { get; init; }
}

/// <summary>
/// A merged-mode read position: skip <see cref="Skip"/> events from the page that starts at <see cref="InnerToken"/>.
/// </summary>
internal sealed record ProviderClientEventsPosition
{
    public required Guid OrgId { get; init; }
    public string? InnerToken { get; init; }
    public int Skip { get; init; }
    public bool Exhausted { get; init; }
}
