using Bit.Core.AdminConsole.Entities.Provider;
using Bit.Core.AdminConsole.Enums.Provider;
using Bit.Core.AdminConsole.Repositories;
using Bit.Core.AdminConsole.Utilities.v2.Results;
using Bit.Core.Enums;
using Bit.Core.Repositories;
using Bit.Core.Services;
using Microsoft.Extensions.Logging;

namespace Bit.Core.AdminConsole.Providers.ClientSeatAutoscale;

public class UpdateProviderClientAutoscaleSettingsCommand(
    IProviderRepository providerRepository,
    IProviderOrganizationRepository providerOrganizationRepository,
    IOrganizationRepository organizationRepository,
    IEventService eventService,
    TimeProvider timeProvider,
    ILogger<UpdateProviderClientAutoscaleSettingsCommand> logger) : IUpdateProviderClientAutoscaleSettingsCommand
{
    public async Task<CommandResult<ProviderOrganization>> UpdateAsync(
        UpdateProviderClientAutoscaleSettingsRequest request)
    {
        var provider = await providerRepository.GetByIdAsync(request.ProviderId);
        if (provider is null)
        {
            return new ProviderNotFound();
        }

        if (provider is not { Type: ProviderType.Msp, Status: ProviderStatusType.Billable, Enabled: true })
        {
            return new ProviderNotEligibleForAutoscale();
        }

        var providerOrganization = await providerOrganizationRepository.GetByOrganizationId(request.OrganizationId);
        if (providerOrganization is null || providerOrganization.ProviderId != provider.Id)
        {
            return new ClientNotFound();
        }

        var organization = await organizationRepository.GetByIdAsync(request.OrganizationId);
        if (organization is not { Status: OrganizationStatusType.Managed, Seats: not null })
        {
            return new ClientNotManagedByProvider();
        }

        if (request.SeatLimit is { } seatLimit && (seatLimit < 1 || seatLimit < organization.Seats.Value))
        {
            return new InvalidAutoscaleSeatLimit();
        }

        var eventType = GetChangeEventType(providerOrganization, request);

        providerOrganization.AutoscaleEnabled = request.Enabled;
        providerOrganization.AutoscaleSeatLimit = request.SeatLimit;
        providerOrganization.RevisionDate = timeProvider.GetUtcNow().UtcDateTime;

        await providerOrganizationRepository.ReplaceAsync(providerOrganization);

        if (eventType is { } type)
        {
            await LogEventAsync(providerOrganization, type);
        }

        return providerOrganization;
    }

    /// <summary>
    /// Returns the one event that describes the change, or <c>null</c> when the save doesn't change what autoscale does.
    /// A limit saved while autoscale stays off has no effect, so it isn't logged.
    /// </summary>
    private static EventType? GetChangeEventType(ProviderOrganization current,
        UpdateProviderClientAutoscaleSettingsRequest request) =>
        (current.AutoscaleEnabled, request.Enabled) switch
        {
            (false, true) => EventType.ProviderOrganization_AutoscaleEnabled,
            (true, false) => EventType.ProviderOrganization_AutoscaleDisabled,
            (true, true) when current.AutoscaleSeatLimit != request.SeatLimit =>
                EventType.ProviderOrganization_AutoscaleLimitUpdated,
            _ => null
        };

    private async Task LogEventAsync(ProviderOrganization providerOrganization, EventType type)
    {
        try
        {
            await eventService.LogProviderOrganizationEventAsync(providerOrganization, type);
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Failed to log {EventType} for provider organization {ProviderOrganizationId}",
                type, providerOrganization.Id);
        }
    }
}
