using Bit.Core.AdminConsole.Entities.Provider;
using Bit.Core.AdminConsole.Models.Data.Provider;
using Bit.Core.Repositories;

#nullable enable

namespace Bit.Core.AdminConsole.Repositories;

public interface IProviderOrganizationRepository : IRepository<ProviderOrganization, Guid>
{
    Task<ICollection<ProviderOrganization>?> CreateManyAsync(IEnumerable<ProviderOrganization> providerOrganizations);
    Task<ICollection<ProviderOrganizationOrganizationDetails>> GetManyDetailsByProviderAsync(Guid providerId);
    Task<ProviderOrganization?> GetByOrganizationId(Guid organizationId);
    Task<IEnumerable<ProviderOrganizationProviderDetails>> GetManyByUserAsync(Guid userId);
    Task<int> GetCountByOrganizationIdsAsync(IEnumerable<Guid> organizationIds);

    /// <summary>
    /// Atomically adds <paramref name="seatsToAdd"/> seats to a provider-managed client organization when the client
    /// has autoscale enabled, stays within its seat limit, and the provider's assigned seats for the client's plan stay
    /// at or below the plan's seat minimum. Concurrent calls for the same provider plan are serialized. Either all of
    /// the seats are added or none are.
    /// </summary>
    /// <param name="organizationId">The client organization.</param>
    /// <param name="planName">The client plan's display name, used to count the provider's assigned seats the same
    /// way consolidated billing does.</param>
    /// <param name="seatsToAdd">The number of seats needed. Must be greater than zero.</param>
    /// <param name="revisionDate">The revision date to stamp on the organization.</param>
    /// <param name="validateOnly">When <c>true</c>, evaluates the rules without writing anything.</param>
    Task<ProviderOrganizationAutoscaleSeatsResult> TryAutoscaleSeatsAsync(Guid organizationId, string planName,
        int seatsToAdd, DateTime revisionDate, bool validateOnly = false);
}
