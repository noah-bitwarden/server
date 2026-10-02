namespace Bit.Core.AdminConsole.Models.Data.Provider;

/// <summary>
/// The outcome of an atomic attempt to add seats to a provider-managed client organization from the provider's seat
/// minimum. Values are returned by the <c>ProviderOrganization_TryAutoscaleSeats</c> stored procedure.
/// </summary>
public enum ProviderOrganizationAutoscaleSeatsResult
{
    Success = 0,
    /// <summary>The provider has not turned autoscale on for this client.</summary>
    NotEnabled = 1,
    /// <summary>The provider has no configured plan with a seat minimum for the client's plan type.</summary>
    NoPool = 2,
    /// <summary>The seats would take the provider's assigned total for the plan above its seat minimum.</summary>
    PoolExhausted = 3,
    /// <summary>The seats would take the client above its <c>AutoscaleSeatLimit</c>.</summary>
    ClientLimitReached = 4,
    /// <summary>The organization is not a managed client of a provider, or has no seat count.</summary>
    ClientNotManaged = 5,
}
