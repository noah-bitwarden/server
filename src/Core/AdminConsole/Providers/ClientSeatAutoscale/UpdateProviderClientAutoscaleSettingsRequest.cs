namespace Bit.Core.AdminConsole.Providers.ClientSeatAutoscale;

/// <summary>
/// Turns seat autoscale on or off for one client of a provider.
/// </summary>
/// <param name="ProviderId">The provider that manages the client.</param>
/// <param name="OrganizationId">The client organization.</param>
/// <param name="Enabled">Whether the client may add seats from the provider's seat minimum when it runs out.</param>
/// <param name="SeatLimit">The most seats autoscale may grow the client to, or <c>null</c> for no per-client limit.</param>
public record UpdateProviderClientAutoscaleSettingsRequest(
    Guid ProviderId,
    Guid OrganizationId,
    bool Enabled,
    int? SeatLimit);
