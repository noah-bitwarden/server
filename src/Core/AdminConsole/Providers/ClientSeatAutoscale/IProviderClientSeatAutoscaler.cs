using Bit.Core.AdminConsole.Entities;

namespace Bit.Core.AdminConsole.Providers.ClientSeatAutoscale;

/// <summary>
/// Covers a seat shortfall in a client organization of a billable MSP from the provider's seat minimum, when a
/// provider admin has turned autoscale on for that client. Never changes the provider's subscription.
/// </summary>
public interface IProviderClientSeatAutoscaler
{
    /// <summary>
    /// Checks whether <paramref name="seatsToAdd"/> seats could be added, without writing anything.
    /// </summary>
    Task<ProviderClientSeatAutoscaleResult> EvaluateAsync(Organization organization, int seatsToAdd);

    /// <summary>
    /// Atomically adds <paramref name="seatsToAdd"/> seats. On success, <see cref="Organization.Seats"/> on
    /// <paramref name="organization"/> is increased to match the database.
    /// </summary>
    Task<ProviderClientSeatAutoscaleResult> TryAutoscaleAsync(Organization organization, int seatsToAdd);
}
