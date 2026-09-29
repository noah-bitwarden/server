namespace Bit.Core.Dirt.Events.ProviderClientEvents.Interfaces;

/// <summary>
/// Reads events across a provider's linked client organizations by fanning out over
/// <see cref="Bit.Core.Repositories.IEventRepository.GetManyByOrganizationAsync"/>, one call per organization.
/// Only client organizations with <c>UseEvents</c> are read; disabled organizations are included.
/// </summary>
public interface IGetProviderClientEventsQuery
{
    /// <summary>
    /// Pages through every eligible client organization in turn, ordered by organization ID and then newest first.
    /// Intended for export. <see cref="ProviderClientEventsRequest.OrganizationIds"/> is honored if present.
    /// </summary>
    Task<ProviderClientEventsPage> GetOrgMajorPageAsync(ProviderClientEventsRequest request);

    /// <summary>
    /// Pages through one client organization, newest first.
    /// </summary>
    /// <exception cref="Bit.Core.Exceptions.NotFoundException">The organization isn't linked to the provider.</exception>
    Task<ProviderClientEventsPage> GetSingleOrgPageAsync(ProviderClientEventsRequest request, Guid organizationId);

    /// <summary>
    /// Pages through up to <see cref="ProviderClientEventsConstants.MergedMaxOrgs"/> client organizations
    /// merged into one newest-first feed.
    /// </summary>
    /// <exception cref="Bit.Core.Exceptions.BadRequestException">More than the maximum number of organizations.</exception>
    Task<ProviderClientEventsPage> GetMergedPageAsync(ProviderClientEventsRequest request);
}
