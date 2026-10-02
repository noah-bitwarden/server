using Bit.Core.AdminConsole.Entities;
using Bit.Core.AdminConsole.Entities.Provider;
using Bit.Core.AdminConsole.Enums.Provider;
using Bit.Core.AdminConsole.Models.Data.Provider;
using Bit.Core.AdminConsole.Models.Mail.Mailer.ProviderClientSeatAutoscale;
using Bit.Core.AdminConsole.Repositories;
using Bit.Core.Billing.Pricing;
using Bit.Core.Enums;
using Bit.Core.Platform.Mail.Mailer;
using Bit.Core.Services;
using Bit.Core.Settings;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ZiggyCreatures.Caching.Fusion;

namespace Bit.Core.AdminConsole.Providers.ClientSeatAutoscale;

public class ProviderClientSeatAutoscaler(
    IFeatureService featureService,
    IGlobalSettings globalSettings,
    IProviderOrganizationRepository providerOrganizationRepository,
    IProviderRepository providerRepository,
    IProviderUserRepository providerUserRepository,
    IPricingClient pricingClient,
    IEventService eventService,
    IMailer mailer,
    [FromKeyedServices(ProviderClientSeatAutoscaler.NotificationCacheName)] IFusionCache notificationCache,
    TimeProvider timeProvider,
    ILogger<ProviderClientSeatAutoscaler> logger) : IProviderClientSeatAutoscaler
{
    public const string NotificationCacheName = "ProviderClientSeatAutoscaleNotifications";

    /// <summary>
    /// How long a blocked-autoscale email suppresses repeats of the same notification.
    /// </summary>
    public static readonly TimeSpan NotificationThrottle = TimeSpan.FromHours(24);

    public Task<ProviderClientSeatAutoscaleResult> EvaluateAsync(Organization organization, int seatsToAdd) =>
        RunAsync(organization, seatsToAdd, validateOnly: true);

    public Task<ProviderClientSeatAutoscaleResult> TryAutoscaleAsync(Organization organization, int seatsToAdd) =>
        RunAsync(organization, seatsToAdd, validateOnly: false);

    private async Task<ProviderClientSeatAutoscaleResult> RunAsync(Organization organization, int seatsToAdd,
        bool validateOnly)
    {
        var client = await GetEligibleClientAsync(organization, seatsToAdd);
        if (client is null)
        {
            return new ProviderClientSeatAutoscaleResult(ProviderClientSeatAutoscaleOutcome.NotApplicable);
        }

        var (providerOrganization, provider) = client.Value;

        if (!providerOrganization.AutoscaleEnabled)
        {
            return new ProviderClientSeatAutoscaleResult(ProviderClientSeatAutoscaleOutcome.NotEnabled);
        }

        var plan = await pricingClient.GetPlanOrThrow(organization.PlanType);

        var result = await providerOrganizationRepository.TryAutoscaleSeatsAsync(
            organization.Id,
            plan.Name,
            seatsToAdd,
            timeProvider.GetUtcNow().UtcDateTime,
            validateOnly);

        var outcome = ToOutcome(result);

        if (outcome == ProviderClientSeatAutoscaleOutcome.Success && !validateOnly)
        {
            organization.Seats += seatsToAdd;
            await LogEventAsync(providerOrganization, EventType.ProviderOrganization_SeatsAutoscaled);
        }

        // Blocked outcomes are logged in both modes because a blocked pre-check ends the request before any commit
        if (outcome is ProviderClientSeatAutoscaleOutcome.PoolExhausted
            or ProviderClientSeatAutoscaleOutcome.ClientLimitReached)
        {
            await LogEventAsync(providerOrganization, outcome == ProviderClientSeatAutoscaleOutcome.PoolExhausted
                ? EventType.ProviderOrganization_SeatAutoscaleBlockedPoolExhausted
                : EventType.ProviderOrganization_SeatAutoscaleBlockedClientLimit);
            await NotifyProviderAdminsAsync(provider, providerOrganization, organization, plan.Name, outcome);
        }

        return new ProviderClientSeatAutoscaleResult(outcome);
    }

    /// <summary>
    /// Returns the client's provider relationship when the organization is a managed client of an enabled, billable MSP
    /// and the feature is on. Every other organization keeps its existing seat behavior.
    /// </summary>
    private async Task<(ProviderOrganization, Provider)?> GetEligibleClientAsync(Organization organization,
        int seatsToAdd)
    {
        if (seatsToAdd < 1 ||
            !featureService.IsEnabled(FeatureFlagKeys.PM18793_ProviderClientSeatAutoscale) ||
            globalSettings.SelfHosted ||
            organization is not { Status: OrganizationStatusType.Managed, Seats: not null })
        {
            return null;
        }

        var providerOrganization = await providerOrganizationRepository.GetByOrganizationId(organization.Id);
        if (providerOrganization is null)
        {
            return null;
        }

        var provider = await providerRepository.GetByIdAsync(providerOrganization.ProviderId);
        if (provider is not { Type: ProviderType.Msp, Status: ProviderStatusType.Billable, Enabled: true })
        {
            return null;
        }

        return (providerOrganization, provider);
    }

    private static ProviderClientSeatAutoscaleOutcome ToOutcome(ProviderOrganizationAutoscaleSeatsResult result) =>
        result switch
        {
            ProviderOrganizationAutoscaleSeatsResult.Success => ProviderClientSeatAutoscaleOutcome.Success,
            ProviderOrganizationAutoscaleSeatsResult.NotEnabled => ProviderClientSeatAutoscaleOutcome.NotEnabled,
            ProviderOrganizationAutoscaleSeatsResult.NoPool => ProviderClientSeatAutoscaleOutcome.NoPool,
            ProviderOrganizationAutoscaleSeatsResult.PoolExhausted => ProviderClientSeatAutoscaleOutcome.PoolExhausted,
            ProviderOrganizationAutoscaleSeatsResult.ClientLimitReached => ProviderClientSeatAutoscaleOutcome.ClientLimitReached,
            _ => ProviderClientSeatAutoscaleOutcome.ClientNotManaged,
        };

    /// <summary>
    /// Logs a provider event. A failure here is logged and never fails the seat request, which may already be committed.
    /// </summary>
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

    /// <summary>
    /// Emails confirmed provider admins at most once per throttle window. A failure here is logged and never fails the
    /// seat request, which may be on a path (SSO, invite links) that hides the error from the provider.
    /// </summary>
    private async Task NotifyProviderAdminsAsync(Provider provider, ProviderOrganization providerOrganization,
        Organization organization, string planName, ProviderClientSeatAutoscaleOutcome outcome)
    {
        try
        {
            var throttleKey = outcome == ProviderClientSeatAutoscaleOutcome.PoolExhausted
                ? $"pool-exhausted:{provider.Id}:{organization.PlanType}"
                : $"client-limit:{organization.Id}";

            if (!await TryClaimNotificationAsync(throttleKey))
            {
                return;
            }

            var adminEmails = (await providerUserRepository.GetManyDetailsByProviderAsync(provider.Id,
                    ProviderUserStatusType.Confirmed))
                .Where(u => u.Type == ProviderUserType.ProviderAdmin && !string.IsNullOrWhiteSpace(u.Email))
                .Select(u => u.Email)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (adminEmails.Count == 0)
            {
                return;
            }

            var clientName = organization.DisplayName();

            await mailer.SendEmail(new ProviderClientSeatAutoscaleBlocked
            {
                ToEmails = adminEmails,
                Subject = $"Seats couldn't be added automatically for {clientName}",
                View = new ProviderClientSeatAutoscaleBlockedView
                {
                    ClientName = clientName,
                    PlanName = planName,
                    Reason = outcome == ProviderClientSeatAutoscaleOutcome.PoolExhausted
                        ? $"All of your provider's {planName} seats within your seat minimum are already assigned to clients."
                        : $"The client reached the autoscale seat limit of {providerOrganization.AutoscaleSeatLimit} that you set for it.",
                    NextSteps = outcome == ProviderClientSeatAutoscaleOutcome.PoolExhausted
                        ? "To make room, reduce seats assigned to another client, add seats to this client from the Provider Portal, or contact Sales about your seat minimum."
                        : "To let this client keep growing, raise its autoscale seat limit or add seats from the Provider Portal.",
                    ClientsUrl = $"{globalSettings.BaseServiceUri.VaultWithHash}/providers/{provider.Id}/clients"
                }
            });
        }
        catch (Exception exception)
        {
            logger.LogError(exception,
                "Failed to notify provider admins that seat autoscale was blocked for organization {OrganizationId}",
                organization.Id);
        }
    }

    /// <summary>
    /// Records that a notification is being sent. Only the caller whose token is stored wins, so concurrent requests on
    /// the same node send one email.
    /// </summary>
    private async Task<bool> TryClaimNotificationAsync(string throttleKey)
    {
        var claim = Guid.NewGuid();
        var stored = await notificationCache.GetOrSetAsync(
            throttleKey,
            claim,
            options => options.SetDuration(NotificationThrottle).SetFailSafe(false));

        return stored == claim;
    }
}
