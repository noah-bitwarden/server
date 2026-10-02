using Bit.Core.AdminConsole.Entities;

namespace Bit.Core.AdminConsole.Providers.ClientSeatAutoscale;

/// <summary>
/// Covers a seat shortfall in a client organization of a billable MSP from the provider's seat minimum, when a
/// provider admin has turned autoscale on for that client. Never changes the provider's subscription.
/// </summary>
public interface IProviderClientSeatAutoscaler
{
    /// <summary>
    /// Checks whether <paramref name="seatsToAdd"/> seats could be added, without adding them. A blocked result is
    /// logged as a provider event and emailed to the provider's admins, so only call this where a blocked result ends
    /// the request.
    /// </summary>
    Task<ProviderClientSeatAutoscaleResult> EvaluateAsync(Organization organization, int seatsToAdd);

    /// <summary>
    /// Atomically adds <paramref name="seatsToAdd"/> seats. On success, <see cref="Organization.Seats"/> on
    /// <paramref name="organization"/> is increased to match the database. Success and blocked results are logged as
    /// provider events; blocked results are also emailed to the provider's admins.
    /// </summary>
    Task<ProviderClientSeatAutoscaleResult> TryAutoscaleAsync(Organization organization, int seatsToAdd);
}
