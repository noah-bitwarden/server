using Bit.Core.AdminConsole.Entities;
using Bit.Core.AdminConsole.Entities.Provider;
using Bit.Core.AdminConsole.Enums.Provider;
using Bit.Core.AdminConsole.Models.Data.Provider;
using Bit.Core.AdminConsole.Repositories;
using Bit.Core.Billing.Enums;
using Bit.Core.Billing.Providers.Entities;
using Bit.Core.Billing.Providers.Repositories;
using Bit.Core.Enums;
using Bit.Core.Repositories;
using Xunit;

namespace Bit.Infrastructure.IntegrationTest.AdminConsole.Repositories;

public class ProviderOrganizationAutoscaleSeatsTests
{
    private const string TeamsPlanName = "Teams (Monthly)";
    private const string EnterprisePlanName = "Enterprise (Monthly)";

    [Theory, DatabaseData]
    public async Task TryAutoscaleSeatsAsync_ExactlyReachesSeatMinimum_AddsSeatsAndAllocatesThem(
        IProviderRepository providerRepository,
        IProviderPlanRepository providerPlanRepository,
        IOrganizationRepository organizationRepository,
        IProviderOrganizationRepository providerOrganizationRepository)
    {
        var provider = await CreateProviderAsync(providerRepository);
        var teamsPlan = await CreateProviderPlanAsync(providerPlanRepository, provider, PlanType.TeamsMonthly, seatMinimum: 10);
        var client = await CreateClientAsync(organizationRepository, providerOrganizationRepository, provider,
            PlanType.TeamsMonthly, seats: 6, autoscaleEnabled: true);
        await CreateClientAsync(organizationRepository, providerOrganizationRepository, provider,
            PlanType.TeamsMonthly, seats: 2, autoscaleEnabled: false);
        var revisionDate = DateTime.UtcNow.AddMinutes(1);

        var result = await providerOrganizationRepository.TryAutoscaleSeatsAsync(client.Id, TeamsPlanName, 2,
            revisionDate);

        Assert.Equal(ProviderOrganizationAutoscaleSeatsResult.Success, result);
        var organization = await organizationRepository.GetByIdAsync(client.Id);
        Assert.Equal(8, organization!.Seats);
        Assert.Equal(revisionDate.ToString("yyyy-MM-dd HH:mm:ss"), organization.RevisionDate.ToString("yyyy-MM-dd HH:mm:ss"));
        var plan = await providerPlanRepository.GetByIdAsync(teamsPlan.Id);
        Assert.Equal(10, plan!.AllocatedSeats);
        Assert.Equal(0, plan.PurchasedSeats);
        Assert.Equal(10, plan.SeatMinimum);
    }

    [Theory, DatabaseData]
    public async Task TryAutoscaleSeatsAsync_OneSeatOverSeatMinimum_ReturnsPoolExhaustedAndChangesNothing(
        IProviderRepository providerRepository,
        IProviderPlanRepository providerPlanRepository,
        IOrganizationRepository organizationRepository,
        IProviderOrganizationRepository providerOrganizationRepository)
    {
        var provider = await CreateProviderAsync(providerRepository);
        var teamsPlan = await CreateProviderPlanAsync(providerPlanRepository, provider, PlanType.TeamsMonthly, seatMinimum: 10);
        var client = await CreateClientAsync(organizationRepository, providerOrganizationRepository, provider,
            PlanType.TeamsMonthly, seats: 10, autoscaleEnabled: true);

        var result = await providerOrganizationRepository.TryAutoscaleSeatsAsync(client.Id, TeamsPlanName, 1,
            DateTime.UtcNow);

        Assert.Equal(ProviderOrganizationAutoscaleSeatsResult.PoolExhausted, result);
        await AssertUnchangedAsync(organizationRepository, providerPlanRepository, client, teamsPlan);
    }

    [Theory, DatabaseData]
    public async Task TryAutoscaleSeatsAsync_MultipleSeatsButPoolCoversOnlySome_AddsNothing(
        IProviderRepository providerRepository,
        IProviderPlanRepository providerPlanRepository,
        IOrganizationRepository organizationRepository,
        IProviderOrganizationRepository providerOrganizationRepository)
    {
        var provider = await CreateProviderAsync(providerRepository);
        var teamsPlan = await CreateProviderPlanAsync(providerPlanRepository, provider, PlanType.TeamsMonthly, seatMinimum: 10);
        var client = await CreateClientAsync(organizationRepository, providerOrganizationRepository, provider,
            PlanType.TeamsMonthly, seats: 8, autoscaleEnabled: true);

        var result = await providerOrganizationRepository.TryAutoscaleSeatsAsync(client.Id, TeamsPlanName, 3,
            DateTime.UtcNow);

        Assert.Equal(ProviderOrganizationAutoscaleSeatsResult.PoolExhausted, result);
        await AssertUnchangedAsync(organizationRepository, providerPlanRepository, client, teamsPlan);
    }

    [Theory, DatabaseData]
    public async Task TryAutoscaleSeatsAsync_TeamsAndEnterprisePoolsAreIndependent(
        IProviderRepository providerRepository,
        IProviderPlanRepository providerPlanRepository,
        IOrganizationRepository organizationRepository,
        IProviderOrganizationRepository providerOrganizationRepository)
    {
        var provider = await CreateProviderAsync(providerRepository);
        await CreateProviderPlanAsync(providerPlanRepository, provider, PlanType.TeamsMonthly, seatMinimum: 5);
        var enterprisePlan = await CreateProviderPlanAsync(providerPlanRepository, provider, PlanType.EnterpriseMonthly,
            seatMinimum: 20);
        var teamsClient = await CreateClientAsync(organizationRepository, providerOrganizationRepository, provider,
            PlanType.TeamsMonthly, seats: 5, autoscaleEnabled: true);
        var enterpriseClient = await CreateClientAsync(organizationRepository, providerOrganizationRepository, provider,
            PlanType.EnterpriseMonthly, seats: 5, autoscaleEnabled: true);

        var teamsResult = await providerOrganizationRepository.TryAutoscaleSeatsAsync(teamsClient.Id, TeamsPlanName, 1,
            DateTime.UtcNow);
        var enterpriseResult = await providerOrganizationRepository.TryAutoscaleSeatsAsync(enterpriseClient.Id,
            EnterprisePlanName, 1, DateTime.UtcNow);

        Assert.Equal(ProviderOrganizationAutoscaleSeatsResult.PoolExhausted, teamsResult);
        Assert.Equal(ProviderOrganizationAutoscaleSeatsResult.Success, enterpriseResult);
        Assert.Equal(6, (await organizationRepository.GetByIdAsync(enterpriseClient.Id))!.Seats);
        Assert.Equal(6, (await providerPlanRepository.GetByIdAsync(enterprisePlan.Id))!.AllocatedSeats);
    }

    [Theory, DatabaseData]
    public async Task TryAutoscaleSeatsAsync_CountsOnlyManagedClientsOnTheSamePlanName(
        IProviderRepository providerRepository,
        IProviderPlanRepository providerPlanRepository,
        IOrganizationRepository organizationRepository,
        IProviderOrganizationRepository providerOrganizationRepository)
    {
        var provider = await CreateProviderAsync(providerRepository);
        var teamsPlan = await CreateProviderPlanAsync(providerPlanRepository, provider, PlanType.TeamsMonthly, seatMinimum: 10);
        var client = await CreateClientAsync(organizationRepository, providerOrganizationRepository, provider,
            PlanType.TeamsMonthly, seats: 5, autoscaleEnabled: true);
        var unmanaged = await CreateClientAsync(organizationRepository, providerOrganizationRepository, provider,
            PlanType.TeamsMonthly, seats: 50, autoscaleEnabled: true);
        unmanaged.Status = OrganizationStatusType.Created;
        await organizationRepository.ReplaceAsync(unmanaged);

        var result = await providerOrganizationRepository.TryAutoscaleSeatsAsync(client.Id, TeamsPlanName, 5,
            DateTime.UtcNow);

        Assert.Equal(ProviderOrganizationAutoscaleSeatsResult.Success, result);
        Assert.Equal(10, (await providerPlanRepository.GetByIdAsync(teamsPlan.Id))!.AllocatedSeats);
    }

    [Theory, DatabaseData]
    public async Task TryAutoscaleSeatsAsync_ClientSeatLimit_IsEnforced(
        IProviderRepository providerRepository,
        IProviderPlanRepository providerPlanRepository,
        IOrganizationRepository organizationRepository,
        IProviderOrganizationRepository providerOrganizationRepository)
    {
        var provider = await CreateProviderAsync(providerRepository);
        var teamsPlan = await CreateProviderPlanAsync(providerPlanRepository, provider, PlanType.TeamsMonthly, seatMinimum: 50);
        var client = await CreateClientAsync(organizationRepository, providerOrganizationRepository, provider,
            PlanType.TeamsMonthly, seats: 5, autoscaleEnabled: true, seatLimit: 7);

        var reachLimit = await providerOrganizationRepository.TryAutoscaleSeatsAsync(client.Id, TeamsPlanName, 2,
            DateTime.UtcNow);
        var passLimit = await providerOrganizationRepository.TryAutoscaleSeatsAsync(client.Id, TeamsPlanName, 1,
            DateTime.UtcNow);

        Assert.Equal(ProviderOrganizationAutoscaleSeatsResult.Success, reachLimit);
        Assert.Equal(ProviderOrganizationAutoscaleSeatsResult.ClientLimitReached, passLimit);
        Assert.Equal(7, (await organizationRepository.GetByIdAsync(client.Id))!.Seats);
        Assert.Equal(7, (await providerPlanRepository.GetByIdAsync(teamsPlan.Id))!.AllocatedSeats);
    }

    [Theory, DatabaseData]
    public async Task TryAutoscaleSeatsAsync_AutoscaleDisabled_ReturnsNotEnabled(
        IProviderRepository providerRepository,
        IProviderPlanRepository providerPlanRepository,
        IOrganizationRepository organizationRepository,
        IProviderOrganizationRepository providerOrganizationRepository)
    {
        var provider = await CreateProviderAsync(providerRepository);
        var teamsPlan = await CreateProviderPlanAsync(providerPlanRepository, provider, PlanType.TeamsMonthly, seatMinimum: 10);
        var client = await CreateClientAsync(organizationRepository, providerOrganizationRepository, provider,
            PlanType.TeamsMonthly, seats: 5, autoscaleEnabled: false);

        var result = await providerOrganizationRepository.TryAutoscaleSeatsAsync(client.Id, TeamsPlanName, 1,
            DateTime.UtcNow);

        Assert.Equal(ProviderOrganizationAutoscaleSeatsResult.NotEnabled, result);
        await AssertUnchangedAsync(organizationRepository, providerPlanRepository, client, teamsPlan);
    }

    [Theory, DatabaseData]
    public async Task TryAutoscaleSeatsAsync_NoSeatMinimum_ReturnsNoPool(
        IProviderRepository providerRepository,
        IProviderPlanRepository providerPlanRepository,
        IOrganizationRepository organizationRepository,
        IProviderOrganizationRepository providerOrganizationRepository)
    {
        var provider = await CreateProviderAsync(providerRepository);
        var teamsPlan = await CreateProviderPlanAsync(providerPlanRepository, provider, PlanType.TeamsMonthly, seatMinimum: null);
        var client = await CreateClientAsync(organizationRepository, providerOrganizationRepository, provider,
            PlanType.TeamsMonthly, seats: 5, autoscaleEnabled: true);

        var result = await providerOrganizationRepository.TryAutoscaleSeatsAsync(client.Id, TeamsPlanName, 1,
            DateTime.UtcNow);

        Assert.Equal(ProviderOrganizationAutoscaleSeatsResult.NoPool, result);
        await AssertUnchangedAsync(organizationRepository, providerPlanRepository, client, teamsPlan);
    }

    [Theory, DatabaseData]
    public async Task TryAutoscaleSeatsAsync_NoProviderPlanForClientPlanType_ReturnsNoPool(
        IProviderRepository providerRepository,
        IProviderPlanRepository providerPlanRepository,
        IOrganizationRepository organizationRepository,
        IProviderOrganizationRepository providerOrganizationRepository)
    {
        var provider = await CreateProviderAsync(providerRepository);
        await CreateProviderPlanAsync(providerPlanRepository, provider, PlanType.EnterpriseMonthly, seatMinimum: 10);
        var client = await CreateClientAsync(organizationRepository, providerOrganizationRepository, provider,
            PlanType.TeamsMonthly, seats: 5, autoscaleEnabled: true);

        var result = await providerOrganizationRepository.TryAutoscaleSeatsAsync(client.Id, TeamsPlanName, 1,
            DateTime.UtcNow);

        Assert.Equal(ProviderOrganizationAutoscaleSeatsResult.NoPool, result);
        Assert.Equal(5, (await organizationRepository.GetByIdAsync(client.Id))!.Seats);
    }

    [Theory, DatabaseData]
    public async Task TryAutoscaleSeatsAsync_ClientNotManaged_ReturnsClientNotManaged(
        IProviderRepository providerRepository,
        IProviderPlanRepository providerPlanRepository,
        IOrganizationRepository organizationRepository,
        IProviderOrganizationRepository providerOrganizationRepository)
    {
        var provider = await CreateProviderAsync(providerRepository);
        await CreateProviderPlanAsync(providerPlanRepository, provider, PlanType.TeamsMonthly, seatMinimum: 10);
        var client = await CreateClientAsync(organizationRepository, providerOrganizationRepository, provider,
            PlanType.TeamsMonthly, seats: 5, autoscaleEnabled: true);
        client.Status = OrganizationStatusType.Created;
        await organizationRepository.ReplaceAsync(client);

        var result = await providerOrganizationRepository.TryAutoscaleSeatsAsync(client.Id, TeamsPlanName, 1,
            DateTime.UtcNow);

        Assert.Equal(ProviderOrganizationAutoscaleSeatsResult.ClientNotManaged, result);
        Assert.Equal(5, (await organizationRepository.GetByIdAsync(client.Id))!.Seats);
    }

    [Theory, DatabaseData]
    public async Task TryAutoscaleSeatsAsync_ValidateOnly_WritesNothing(
        IProviderRepository providerRepository,
        IProviderPlanRepository providerPlanRepository,
        IOrganizationRepository organizationRepository,
        IProviderOrganizationRepository providerOrganizationRepository)
    {
        var provider = await CreateProviderAsync(providerRepository);
        var teamsPlan = await CreateProviderPlanAsync(providerPlanRepository, provider, PlanType.TeamsMonthly, seatMinimum: 10);
        var client = await CreateClientAsync(organizationRepository, providerOrganizationRepository, provider,
            PlanType.TeamsMonthly, seats: 5, autoscaleEnabled: true);

        var result = await providerOrganizationRepository.TryAutoscaleSeatsAsync(client.Id, TeamsPlanName, 3,
            DateTime.UtcNow, validateOnly: true);

        Assert.Equal(ProviderOrganizationAutoscaleSeatsResult.Success, result);
        await AssertUnchangedAsync(organizationRepository, providerPlanRepository, client, teamsPlan);
    }

    [Theory, DatabaseData]
    public async Task TryAutoscaleSeatsAsync_NeverTouchesSubscriptionFields(
        IProviderRepository providerRepository,
        IProviderPlanRepository providerPlanRepository,
        IOrganizationRepository organizationRepository,
        IProviderOrganizationRepository providerOrganizationRepository)
    {
        var provider = await CreateProviderAsync(providerRepository);
        await CreateProviderPlanAsync(providerPlanRepository, provider, PlanType.TeamsMonthly, seatMinimum: 10);
        var client = await CreateClientAsync(organizationRepository, providerOrganizationRepository, provider,
            PlanType.TeamsMonthly, seats: 5, autoscaleEnabled: true);
        client.MaxAutoscaleSeats = 6;
        await organizationRepository.ReplaceAsync(client);

        var result = await providerOrganizationRepository.TryAutoscaleSeatsAsync(client.Id, TeamsPlanName, 3,
            DateTime.UtcNow);

        Assert.Equal(ProviderOrganizationAutoscaleSeatsResult.Success, result);
        var organization = await organizationRepository.GetByIdAsync(client.Id);
        Assert.Equal(8, organization!.Seats);
        Assert.Equal(6, organization.MaxAutoscaleSeats);
        Assert.False(organization.SyncSeats);
    }

    [Theory, DatabaseData]
    public async Task TryAutoscaleSeatsAsync_ConcurrentRequestsForTheLastSeat_OnlyOneSucceeds(
        IProviderRepository providerRepository,
        IProviderPlanRepository providerPlanRepository,
        IOrganizationRepository organizationRepository,
        IProviderOrganizationRepository providerOrganizationRepository)
    {
        var provider = await CreateProviderAsync(providerRepository);
        var teamsPlan = await CreateProviderPlanAsync(providerPlanRepository, provider, PlanType.TeamsMonthly, seatMinimum: 10);
        var firstClient = await CreateClientAsync(organizationRepository, providerOrganizationRepository, provider,
            PlanType.TeamsMonthly, seats: 5, autoscaleEnabled: true);
        var secondClient = await CreateClientAsync(organizationRepository, providerOrganizationRepository, provider,
            PlanType.TeamsMonthly, seats: 4, autoscaleEnabled: true);

        var results = await Task.WhenAll(
            providerOrganizationRepository.TryAutoscaleSeatsAsync(firstClient.Id, TeamsPlanName, 1, DateTime.UtcNow),
            providerOrganizationRepository.TryAutoscaleSeatsAsync(secondClient.Id, TeamsPlanName, 1, DateTime.UtcNow));

        Assert.Single(results, r => r == ProviderOrganizationAutoscaleSeatsResult.Success);
        Assert.Single(results, r => r == ProviderOrganizationAutoscaleSeatsResult.PoolExhausted);
        var totalSeats = (await organizationRepository.GetByIdAsync(firstClient.Id))!.Seats +
                         (await organizationRepository.GetByIdAsync(secondClient.Id))!.Seats;
        Assert.Equal(10, totalSeats);
        Assert.Equal(10, (await providerPlanRepository.GetByIdAsync(teamsPlan.Id))!.AllocatedSeats);
    }

    private static Task<Provider> CreateProviderAsync(IProviderRepository providerRepository) =>
        providerRepository.CreateAsync(new Provider
        {
            Name = "Autoscale Provider",
            BillingEmail = $"billing+{Guid.NewGuid()}@example.com",
            Type = ProviderType.Msp,
            Status = ProviderStatusType.Billable,
            Enabled = true
        });

    private static Task<ProviderPlan> CreateProviderPlanAsync(IProviderPlanRepository providerPlanRepository,
        Provider provider, PlanType planType, int? seatMinimum) =>
        providerPlanRepository.CreateAsync(new ProviderPlan
        {
            ProviderId = provider.Id,
            PlanType = planType,
            SeatMinimum = seatMinimum,
            PurchasedSeats = 0,
            AllocatedSeats = 0
        });

    private static async Task<Organization> CreateClientAsync(IOrganizationRepository organizationRepository,
        IProviderOrganizationRepository providerOrganizationRepository, Provider provider, PlanType planType,
        int seats, bool autoscaleEnabled, int? seatLimit = null)
    {
        var organization = await organizationRepository.CreateAsync(new Organization
        {
            Name = $"Client {Guid.NewGuid()}",
            BillingEmail = $"billing+{Guid.NewGuid()}@example.com",
            Plan = planType == PlanType.TeamsMonthly ? TeamsPlanName : EnterprisePlanName,
            PlanType = planType,
            Status = OrganizationStatusType.Managed,
            Enabled = true,
            Seats = seats
        });

        await providerOrganizationRepository.CreateAsync(new ProviderOrganization
        {
            ProviderId = provider.Id,
            OrganizationId = organization.Id,
            AutoscaleEnabled = autoscaleEnabled,
            AutoscaleSeatLimit = seatLimit
        });

        return organization;
    }

    private static async Task AssertUnchangedAsync(IOrganizationRepository organizationRepository,
        IProviderPlanRepository providerPlanRepository, Organization client, ProviderPlan providerPlan)
    {
        Assert.Equal(client.Seats, (await organizationRepository.GetByIdAsync(client.Id))!.Seats);
        Assert.Equal(providerPlan.AllocatedSeats,
            (await providerPlanRepository.GetByIdAsync(providerPlan.Id))!.AllocatedSeats);
    }
}
