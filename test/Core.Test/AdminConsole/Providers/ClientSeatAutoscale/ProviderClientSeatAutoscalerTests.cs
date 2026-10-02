using Bit.Core.AdminConsole.Entities;
using Bit.Core.AdminConsole.Entities.Provider;
using Bit.Core.AdminConsole.Enums.Provider;
using Bit.Core.AdminConsole.Models.Data.Provider;
using Bit.Core.AdminConsole.Models.Mail.Mailer.ProviderClientSeatAutoscale;
using Bit.Core.AdminConsole.Providers.ClientSeatAutoscale;
using Bit.Core.AdminConsole.Repositories;
using Bit.Core.Billing.Enums;
using Bit.Core.Billing.Pricing;
using Bit.Core.Enums;
using Bit.Core.Platform.Mail.Mailer;
using Bit.Core.Services;
using Bit.Core.Settings;
using Bit.Core.Test.Billing.Mocks;
using Bit.Test.Common.AutoFixture;
using Bit.Test.Common.AutoFixture.Attributes;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;
using ZiggyCreatures.Caching.Fusion;

namespace Bit.Core.Test.AdminConsole.Providers.ClientSeatAutoscale;

public class ProviderClientSeatAutoscalerTests
{
    [Theory, BitAutoData]
    public async Task TryAutoscaleAsync_EligibleClient_AddsSeatsAndLogsEvent(Organization organization, Provider provider)
    {
        var (sutProvider, providerOrganization) = Arrange(organization, provider);
        SetRepositoryResult(sutProvider, ProviderOrganizationAutoscaleSeatsResult.Success);

        var result = await sutProvider.Sut.TryAutoscaleAsync(organization, 3);

        Assert.Equal(ProviderClientSeatAutoscaleOutcome.Success, result.Outcome);
        Assert.True(result.Applies);
        Assert.True(result.Succeeded);
        Assert.Equal(13, organization.Seats);
        await sutProvider.GetDependency<IProviderOrganizationRepository>().Received(1)
            .TryAutoscaleSeatsAsync(organization.Id, MockPlans.Get(PlanType.TeamsMonthly).Name, 3,
                Arg.Any<DateTime>(), false);
        await AssertLoggedOnlyAsync(sutProvider, providerOrganization, EventType.ProviderOrganization_SeatsAutoscaled);
    }

    [Theory, BitAutoData]
    public async Task EvaluateAsync_EligibleClient_ValidatesWithoutChangingSeats(Organization organization,
        Provider provider)
    {
        var (sutProvider, _) = Arrange(organization, provider);
        SetRepositoryResult(sutProvider, ProviderOrganizationAutoscaleSeatsResult.Success);

        var result = await sutProvider.Sut.EvaluateAsync(organization, 3);

        Assert.True(result.Succeeded);
        Assert.Equal(10, organization.Seats);
        await sutProvider.GetDependency<IProviderOrganizationRepository>().Received(1)
            .TryAutoscaleSeatsAsync(organization.Id, Arg.Any<string>(), 3, Arg.Any<DateTime>(), true);
        await sutProvider.GetDependency<IEventService>().DidNotReceiveWithAnyArgs()
            .LogProviderOrganizationEventAsync(default!, default);
    }

    [Theory]
    [BitAutoData(ProviderOrganizationAutoscaleSeatsResult.NotEnabled, ProviderClientSeatAutoscaleOutcome.NotEnabled)]
    [BitAutoData(ProviderOrganizationAutoscaleSeatsResult.NoPool, ProviderClientSeatAutoscaleOutcome.NoPool)]
    [BitAutoData(ProviderOrganizationAutoscaleSeatsResult.PoolExhausted, ProviderClientSeatAutoscaleOutcome.PoolExhausted)]
    [BitAutoData(ProviderOrganizationAutoscaleSeatsResult.ClientLimitReached, ProviderClientSeatAutoscaleOutcome.ClientLimitReached)]
    [BitAutoData(ProviderOrganizationAutoscaleSeatsResult.ClientNotManaged, ProviderClientSeatAutoscaleOutcome.ClientNotManaged)]
    public async Task TryAutoscaleAsync_RepositoryRejects_ReturnsFailureWithoutChangingSeats(
        ProviderOrganizationAutoscaleSeatsResult repositoryResult,
        ProviderClientSeatAutoscaleOutcome expectedOutcome,
        Organization organization,
        Provider provider)
    {
        var (sutProvider, _) = Arrange(organization, provider);
        SetRepositoryResult(sutProvider, repositoryResult);

        var result = await sutProvider.Sut.TryAutoscaleAsync(organization, 1);

        Assert.Equal(expectedOutcome, result.Outcome);
        Assert.True(result.Applies);
        Assert.False(result.Succeeded);
        Assert.Equal(10, organization.Seats);
        await sutProvider.GetDependency<IEventService>().DidNotReceive()
            .LogProviderOrganizationEventAsync(Arg.Any<ProviderOrganization>(),
                EventType.ProviderOrganization_SeatsAutoscaled);
    }

    [Theory]
    [BitAutoData(ProviderOrganizationAutoscaleSeatsResult.PoolExhausted,
        EventType.ProviderOrganization_SeatAutoscaleBlockedPoolExhausted)]
    [BitAutoData(ProviderOrganizationAutoscaleSeatsResult.ClientLimitReached,
        EventType.ProviderOrganization_SeatAutoscaleBlockedClientLimit)]
    public async Task TryAutoscaleAsync_Blocked_LogsBlockedEventOnce(ProviderOrganizationAutoscaleSeatsResult repositoryResult,
        EventType expectedEvent, Organization organization, Provider provider)
    {
        var (sutProvider, providerOrganization) = Arrange(organization, provider);
        SetRepositoryResult(sutProvider, repositoryResult);

        await sutProvider.Sut.TryAutoscaleAsync(organization, 1);

        await AssertLoggedOnlyAsync(sutProvider, providerOrganization, expectedEvent);
    }

    [Theory]
    [BitAutoData(ProviderOrganizationAutoscaleSeatsResult.PoolExhausted,
        EventType.ProviderOrganization_SeatAutoscaleBlockedPoolExhausted)]
    [BitAutoData(ProviderOrganizationAutoscaleSeatsResult.ClientLimitReached,
        EventType.ProviderOrganization_SeatAutoscaleBlockedClientLimit)]
    public async Task EvaluateAsync_Blocked_LogsBlockedEventOnce(ProviderOrganizationAutoscaleSeatsResult repositoryResult,
        EventType expectedEvent, Organization organization, Provider provider)
    {
        var (sutProvider, providerOrganization) = Arrange(organization, provider);
        SetRepositoryResult(sutProvider, repositoryResult);

        await sutProvider.Sut.EvaluateAsync(organization, 1);

        await AssertLoggedOnlyAsync(sutProvider, providerOrganization, expectedEvent);
    }

    [Theory]
    [BitAutoData(ProviderOrganizationAutoscaleSeatsResult.NotEnabled)]
    [BitAutoData(ProviderOrganizationAutoscaleSeatsResult.NoPool)]
    [BitAutoData(ProviderOrganizationAutoscaleSeatsResult.ClientNotManaged)]
    public async Task TryAutoscaleAsync_NotActionableFailure_DoesNotLogEvent(
        ProviderOrganizationAutoscaleSeatsResult repositoryResult, Organization organization, Provider provider)
    {
        var (sutProvider, _) = Arrange(organization, provider);
        SetRepositoryResult(sutProvider, repositoryResult);

        await sutProvider.Sut.TryAutoscaleAsync(organization, 1);
        await sutProvider.Sut.EvaluateAsync(organization, 1);

        await sutProvider.GetDependency<IEventService>().DidNotReceiveWithAnyArgs()
            .LogProviderOrganizationEventAsync(default!, default);
    }

    [Theory, BitAutoData]
    public async Task TryAutoscaleAsync_BlockedRepeatedly_LogsEveryTimeButEmailsOnce(Organization organization,
        Provider provider)
    {
        var (sutProvider, providerOrganization) = Arrange(organization, provider);
        SetRepositoryResult(sutProvider, ProviderOrganizationAutoscaleSeatsResult.PoolExhausted);

        await sutProvider.Sut.EvaluateAsync(organization, 1);
        await sutProvider.Sut.TryAutoscaleAsync(organization, 1);
        await sutProvider.Sut.TryAutoscaleAsync(organization, 1);

        await sutProvider.GetDependency<IEventService>().Received(3)
            .LogProviderOrganizationEventAsync(providerOrganization,
                EventType.ProviderOrganization_SeatAutoscaleBlockedPoolExhausted);
        await sutProvider.GetDependency<IMailer>().ReceivedWithAnyArgs(1)
            .SendEmail(default(ProviderClientSeatAutoscaleBlocked)!);
    }

    [Theory, BitAutoData]
    public async Task TryAutoscaleAsync_EventLoggingFailsAfterSeatsAdded_StillSucceeds(Organization organization,
        Provider provider)
    {
        var (sutProvider, _) = Arrange(organization, provider);
        SetRepositoryResult(sutProvider, ProviderOrganizationAutoscaleSeatsResult.Success);
        SetEventServiceThrows(sutProvider);

        var result = await sutProvider.Sut.TryAutoscaleAsync(organization, 2);

        Assert.Equal(ProviderClientSeatAutoscaleOutcome.Success, result.Outcome);
        Assert.Equal(12, organization.Seats);
    }

    [Theory, BitAutoData]
    public async Task TryAutoscaleAsync_EventLoggingFailsWhenBlocked_StillReturnsOutcomeAndEmails(
        Organization organization, Provider provider)
    {
        var (sutProvider, _) = Arrange(organization, provider);
        SetRepositoryResult(sutProvider, ProviderOrganizationAutoscaleSeatsResult.ClientLimitReached);
        SetEventServiceThrows(sutProvider);

        var result = await sutProvider.Sut.TryAutoscaleAsync(organization, 1);

        Assert.Equal(ProviderClientSeatAutoscaleOutcome.ClientLimitReached, result.Outcome);
        await sutProvider.GetDependency<IMailer>().ReceivedWithAnyArgs(1)
            .SendEmail(default(ProviderClientSeatAutoscaleBlocked)!);
    }

    [Theory, BitAutoData]
    public async Task TryAutoscaleAsync_AutoscaleDisabled_ReturnsNotEnabledWithoutCallingRepository(
        Organization organization, Provider provider)
    {
        var (sutProvider, providerOrganization) = Arrange(organization, provider);
        providerOrganization.AutoscaleEnabled = false;

        var result = await sutProvider.Sut.TryAutoscaleAsync(organization, 1);

        Assert.Equal(ProviderClientSeatAutoscaleOutcome.NotEnabled, result.Outcome);
        await sutProvider.GetDependency<IProviderOrganizationRepository>().DidNotReceiveWithAnyArgs()
            .TryAutoscaleSeatsAsync(default, default!, default, default, default);
        await sutProvider.GetDependency<IEventService>().DidNotReceiveWithAnyArgs()
            .LogProviderOrganizationEventAsync(default!, default);
    }

    [Theory, BitAutoData]
    public async Task TryAutoscaleAsync_FeatureFlagOff_NotApplicable(Organization organization, Provider provider)
    {
        var (sutProvider, _) = Arrange(organization, provider);
        sutProvider.GetDependency<IFeatureService>()
            .IsEnabled(FeatureFlagKeys.PM18793_ProviderClientSeatAutoscale).Returns(false);

        await AssertNotApplicableAsync(sutProvider, organization);
    }

    [Theory, BitAutoData]
    public async Task TryAutoscaleAsync_SelfHosted_NotApplicable(Organization organization, Provider provider)
    {
        var (sutProvider, _) = Arrange(organization, provider);
        sutProvider.GetDependency<IGlobalSettings>().SelfHosted.Returns(true);

        await AssertNotApplicableAsync(sutProvider, organization);
    }

    [Theory]
    [BitAutoData(OrganizationStatusType.Created)]
    [BitAutoData(OrganizationStatusType.Pending)]
    public async Task TryAutoscaleAsync_OrganizationNotManaged_NotApplicable(OrganizationStatusType status,
        Organization organization, Provider provider)
    {
        var (sutProvider, _) = Arrange(organization, provider);
        organization.Status = status;

        await AssertNotApplicableAsync(sutProvider, organization);
    }

    [Theory, BitAutoData]
    public async Task TryAutoscaleAsync_OrganizationHasNoSeatCount_NotApplicable(Organization organization,
        Provider provider)
    {
        var (sutProvider, _) = Arrange(organization, provider);
        organization.Seats = null;

        await AssertNotApplicableAsync(sutProvider, organization);
    }

    [Theory, BitAutoData]
    public async Task TryAutoscaleAsync_NoProviderOrganization_NotApplicable(Organization organization,
        Provider provider)
    {
        var (sutProvider, _) = Arrange(organization, provider);
        sutProvider.GetDependency<IProviderOrganizationRepository>().GetByOrganizationId(organization.Id)
            .Returns((ProviderOrganization?)null);

        await AssertNotApplicableAsync(sutProvider, organization);
    }

    [Theory]
    [BitAutoData(ProviderType.Reseller, ProviderStatusType.Billable, true)]
    [BitAutoData(ProviderType.BusinessUnit, ProviderStatusType.Billable, true)]
    [BitAutoData(ProviderType.Msp, ProviderStatusType.Created, true)]
    [BitAutoData(ProviderType.Msp, ProviderStatusType.Pending, true)]
    [BitAutoData(ProviderType.Msp, ProviderStatusType.Billable, false)]
    public async Task TryAutoscaleAsync_ProviderNotEligible_NotApplicable(ProviderType type,
        ProviderStatusType status, bool enabled, Organization organization, Provider provider)
    {
        var (sutProvider, _) = Arrange(organization, provider);
        provider.Type = type;
        provider.Status = status;
        provider.Enabled = enabled;

        await AssertNotApplicableAsync(sutProvider, organization);
    }

    [Theory, BitAutoData]
    public async Task TryAutoscaleAsync_NoSeatsRequested_NotApplicable(Organization organization, Provider provider)
    {
        var (sutProvider, _) = Arrange(organization, provider);

        var result = await sutProvider.Sut.TryAutoscaleAsync(organization, 0);

        Assert.False(result.Applies);
    }

    [Theory, BitAutoData]
    public async Task TryAutoscaleAsync_PoolExhausted_EmailsConfirmedProviderAdminsOnly(Organization organization,
        Provider provider)
    {
        var (sutProvider, _) = Arrange(organization, provider);
        SetRepositoryResult(sutProvider, ProviderOrganizationAutoscaleSeatsResult.PoolExhausted);
        sutProvider.GetDependency<IProviderUserRepository>()
            .GetManyDetailsByProviderAsync(provider.Id, ProviderUserStatusType.Confirmed)
            .Returns([
                new ProviderUserUserDetails { Email = "admin@example.com", Type = ProviderUserType.ProviderAdmin },
                new ProviderUserUserDetails { Email = "service@example.com", Type = ProviderUserType.ServiceUser }
            ]);

        await sutProvider.Sut.TryAutoscaleAsync(organization, 1);

        await sutProvider.GetDependency<IMailer>().Received(1).SendEmail(
            Arg.Is<ProviderClientSeatAutoscaleBlocked>(mail =>
                mail.ToEmails.SequenceEqual(new[] { "admin@example.com" }) &&
                mail.View.ClientsUrl.EndsWith($"/providers/{provider.Id}/clients") &&
                mail.View.PlanName == MockPlans.Get(PlanType.TeamsMonthly).Name));
    }

    [Theory, BitAutoData]
    public async Task TryAutoscaleAsync_PoolExhaustedTwice_EmailsOnce(Organization organization, Provider provider)
    {
        var (sutProvider, _) = Arrange(organization, provider);
        SetRepositoryResult(sutProvider, ProviderOrganizationAutoscaleSeatsResult.PoolExhausted);

        await sutProvider.Sut.TryAutoscaleAsync(organization, 1);
        await sutProvider.Sut.EvaluateAsync(organization, 1);

        await sutProvider.GetDependency<IMailer>().ReceivedWithAnyArgs(1)
            .SendEmail(default(ProviderClientSeatAutoscaleBlocked)!);
    }

    [Theory, BitAutoData]
    public async Task TryAutoscaleAsync_PoolExhaustedOnDifferentPlans_EmailsForEach(Organization organization,
        Organization otherPlanOrganization, Provider provider)
    {
        var (sutProvider, _) = Arrange(organization, provider);
        ArrangeAdditionalClient(sutProvider, otherPlanOrganization, provider, PlanType.EnterpriseMonthly);
        SetRepositoryResult(sutProvider, ProviderOrganizationAutoscaleSeatsResult.PoolExhausted);

        await sutProvider.Sut.TryAutoscaleAsync(organization, 1);
        await sutProvider.Sut.TryAutoscaleAsync(otherPlanOrganization, 1);

        await sutProvider.GetDependency<IMailer>().ReceivedWithAnyArgs(2)
            .SendEmail(default(ProviderClientSeatAutoscaleBlocked)!);
    }

    [Theory, BitAutoData]
    public async Task TryAutoscaleAsync_PoolExhaustedForTwoClientsOnSamePlan_EmailsOnce(Organization organization,
        Organization samePlanOrganization, Provider provider)
    {
        var (sutProvider, _) = Arrange(organization, provider);
        ArrangeAdditionalClient(sutProvider, samePlanOrganization, provider, PlanType.TeamsMonthly);
        SetRepositoryResult(sutProvider, ProviderOrganizationAutoscaleSeatsResult.PoolExhausted);

        await sutProvider.Sut.TryAutoscaleAsync(organization, 1);
        await sutProvider.Sut.TryAutoscaleAsync(samePlanOrganization, 1);

        await sutProvider.GetDependency<IMailer>().ReceivedWithAnyArgs(1)
            .SendEmail(default(ProviderClientSeatAutoscaleBlocked)!);
    }

    [Theory, BitAutoData]
    public async Task TryAutoscaleAsync_ClientLimitReachedForTwoClients_EmailsForEach(Organization organization,
        Organization otherOrganization, Provider provider)
    {
        var (sutProvider, _) = Arrange(organization, provider);
        ArrangeAdditionalClient(sutProvider, otherOrganization, provider, PlanType.TeamsMonthly);
        SetRepositoryResult(sutProvider, ProviderOrganizationAutoscaleSeatsResult.ClientLimitReached);

        await sutProvider.Sut.TryAutoscaleAsync(organization, 1);
        await sutProvider.Sut.TryAutoscaleAsync(organization, 1);
        await sutProvider.Sut.TryAutoscaleAsync(otherOrganization, 1);

        await sutProvider.GetDependency<IMailer>().ReceivedWithAnyArgs(2)
            .SendEmail(default(ProviderClientSeatAutoscaleBlocked)!);
    }

    [Theory]
    [BitAutoData(ProviderOrganizationAutoscaleSeatsResult.Success)]
    [BitAutoData(ProviderOrganizationAutoscaleSeatsResult.NoPool)]
    [BitAutoData(ProviderOrganizationAutoscaleSeatsResult.NotEnabled)]
    [BitAutoData(ProviderOrganizationAutoscaleSeatsResult.ClientNotManaged)]
    public async Task TryAutoscaleAsync_OutcomeWithoutNotification_DoesNotEmail(
        ProviderOrganizationAutoscaleSeatsResult repositoryResult, Organization organization, Provider provider)
    {
        var (sutProvider, _) = Arrange(organization, provider);
        SetRepositoryResult(sutProvider, repositoryResult);

        await sutProvider.Sut.TryAutoscaleAsync(organization, 1);

        await sutProvider.GetDependency<IMailer>().DidNotReceiveWithAnyArgs()
            .SendEmail(default(ProviderClientSeatAutoscaleBlocked)!);
    }

    [Theory, BitAutoData]
    public async Task TryAutoscaleAsync_MailerThrows_StillReturnsOutcome(Organization organization, Provider provider)
    {
        var (sutProvider, _) = Arrange(organization, provider);
        SetRepositoryResult(sutProvider, ProviderOrganizationAutoscaleSeatsResult.PoolExhausted);
        sutProvider.GetDependency<IMailer>().SendEmail(Arg.Any<ProviderClientSeatAutoscaleBlocked>())
            .ThrowsAsync(new Exception("mail is down"));

        var result = await sutProvider.Sut.TryAutoscaleAsync(organization, 1);

        Assert.Equal(ProviderClientSeatAutoscaleOutcome.PoolExhausted, result.Outcome);
    }

    [Theory, BitAutoData]
    public async Task TryAutoscaleAsync_NoConfirmedAdmins_DoesNotEmail(Organization organization, Provider provider)
    {
        var (sutProvider, _) = Arrange(organization, provider);
        SetRepositoryResult(sutProvider, ProviderOrganizationAutoscaleSeatsResult.ClientLimitReached);
        sutProvider.GetDependency<IProviderUserRepository>()
            .GetManyDetailsByProviderAsync(provider.Id, ProviderUserStatusType.Confirmed)
            .Returns([new ProviderUserUserDetails { Email = "service@example.com", Type = ProviderUserType.ServiceUser }]);

        await sutProvider.Sut.TryAutoscaleAsync(organization, 1);

        await sutProvider.GetDependency<IMailer>().DidNotReceiveWithAnyArgs()
            .SendEmail(default(ProviderClientSeatAutoscaleBlocked)!);
    }

    [Fact]
    public void DefaultResult_IsNotApplicable()
    {
        var result = default(ProviderClientSeatAutoscaleResult);

        Assert.Equal(ProviderClientSeatAutoscaleOutcome.NotApplicable, result.Outcome);
        Assert.False(result.Applies);
        Assert.False(result.Succeeded);
    }

    private static (SutProvider<ProviderClientSeatAutoscaler>, ProviderOrganization) Arrange(
        Organization organization, Provider provider)
    {
        var sutProvider = new SutProvider<ProviderClientSeatAutoscaler>()
            .SetDependency<IFusionCache>(new FusionCache(new FusionCacheOptions()))
            .Create();

        provider.Type = ProviderType.Msp;
        provider.Status = ProviderStatusType.Billable;
        provider.Enabled = true;
        sutProvider.GetDependency<IProviderRepository>().GetByIdAsync(provider.Id).Returns(provider);

        sutProvider.GetDependency<IFeatureService>()
            .IsEnabled(FeatureFlagKeys.PM18793_ProviderClientSeatAutoscale).Returns(true);
        sutProvider.GetDependency<IGlobalSettings>().BaseServiceUri.VaultWithHash.Returns("https://vault.example.com/#");
        sutProvider.GetDependency<IPricingClient>().GetPlanOrThrow(Arg.Any<PlanType>())
            .Returns(call => MockPlans.Get(call.Arg<PlanType>()));
        sutProvider.GetDependency<IProviderUserRepository>()
            .GetManyDetailsByProviderAsync(provider.Id, ProviderUserStatusType.Confirmed)
            .Returns([new ProviderUserUserDetails { Email = "admin@example.com", Type = ProviderUserType.ProviderAdmin }]);

        var providerOrganization = ArrangeAdditionalClient(sutProvider, organization, provider, PlanType.TeamsMonthly);

        return (sutProvider, providerOrganization);
    }

    private static ProviderOrganization ArrangeAdditionalClient(SutProvider<ProviderClientSeatAutoscaler> sutProvider,
        Organization organization, Provider provider, PlanType planType)
    {
        organization.Status = OrganizationStatusType.Managed;
        organization.Seats = 10;
        organization.PlanType = planType;

        var providerOrganization = new ProviderOrganization
        {
            Id = Guid.NewGuid(),
            ProviderId = provider.Id,
            OrganizationId = organization.Id,
            AutoscaleEnabled = true,
            AutoscaleSeatLimit = 20
        };
        sutProvider.GetDependency<IProviderOrganizationRepository>().GetByOrganizationId(organization.Id)
            .Returns(providerOrganization);

        return providerOrganization;
    }

    private static void SetRepositoryResult(SutProvider<ProviderClientSeatAutoscaler> sutProvider,
        ProviderOrganizationAutoscaleSeatsResult result) =>
        sutProvider.GetDependency<IProviderOrganizationRepository>()
            .TryAutoscaleSeatsAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<DateTime>(),
                Arg.Any<bool>())
            .Returns(result);

    private static async Task AssertNotApplicableAsync(SutProvider<ProviderClientSeatAutoscaler> sutProvider,
        Organization organization)
    {
        var tryResult = await sutProvider.Sut.TryAutoscaleAsync(organization, 1);
        var evaluateResult = await sutProvider.Sut.EvaluateAsync(organization, 1);

        Assert.Equal(ProviderClientSeatAutoscaleOutcome.NotApplicable, tryResult.Outcome);
        Assert.Equal(ProviderClientSeatAutoscaleOutcome.NotApplicable, evaluateResult.Outcome);
        await sutProvider.GetDependency<IProviderOrganizationRepository>().DidNotReceiveWithAnyArgs()
            .TryAutoscaleSeatsAsync(default, default!, default, default, default);
        await sutProvider.GetDependency<IEventService>().DidNotReceiveWithAnyArgs()
            .LogProviderOrganizationEventAsync(default!, default);
    }

    private static async Task AssertLoggedOnlyAsync(SutProvider<ProviderClientSeatAutoscaler> sutProvider,
        ProviderOrganization providerOrganization, EventType eventType)
    {
        var eventService = sutProvider.GetDependency<IEventService>();
        await eventService.Received(1).LogProviderOrganizationEventAsync(providerOrganization, eventType);
        await eventService.ReceivedWithAnyArgs(1).LogProviderOrganizationEventAsync(default!, default);
    }

    private static void SetEventServiceThrows(SutProvider<ProviderClientSeatAutoscaler> sutProvider) =>
        sutProvider.GetDependency<IEventService>()
            .LogProviderOrganizationEventAsync(Arg.Any<ProviderOrganization>(), Arg.Any<EventType>())
            .ThrowsAsync(new Exception("events are down"));
}
