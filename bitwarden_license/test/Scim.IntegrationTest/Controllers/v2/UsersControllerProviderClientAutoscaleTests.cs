using System.Text.Json;
using Bit.Core;
using Bit.Core.AdminConsole.Enums.Provider;
using Bit.Core.AdminConsole.Providers.ClientSeatAutoscale;
using Bit.Core.Billing.Enums;
using Bit.Core.Billing.Pricing;
using Bit.Core.Enums;
using Bit.Core.Models.StaticStore;
using Bit.Core.Services;
using Bit.Infrastructure.EntityFramework.AdminConsole.Models.Provider;
using Bit.Infrastructure.EntityFramework.Billing.Models;
using Bit.Scim.IntegrationTest.Factories;
using Bit.Scim.Models;
using Bit.Scim.Utilities;
using NSubstitute;
using Xunit;

namespace Bit.Scim.IntegrationTest.Controllers.v2;

/// <summary>
/// SCIM provisioning into a client organization of a billable MSP that has every seat taken. The seeded organization
/// has three occupied seats.
/// </summary>
public class UsersControllerProviderClientAutoscaleTests
{
    private const int OccupiedSeats = 3;

    [Theory]
    [InlineData(false, "Seat limit has been reached. Please contact your provider to add more seats.")]
    [InlineData(true, "Seat limit of 3 has been reached. Contact your provider to purchase additional seats.")]
    public async Task Post_ProviderClientOutOfSeats_AutoscaleFlagOff_KeepsExistingResponse(
        bool scimInviteUserOptimization, string expectedDetail)
    {
        var factory = CreateFactory(scimInviteUserOptimization, providerClientSeatAutoscale: false);
        SeedProviderClient(factory, seatMinimum: 10, autoscaleEnabled: true);

        var context = await factory.UsersPostAsync(ScimApplicationFactory.TestOrganizationId1, NewUser());

        await AssertScimErrorAsync(context, StatusCodes.Status400BadRequest, expectedDetail);
        Assert.Equal(OccupiedSeats, GetClientSeats(factory));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Post_ProviderClientOutOfSeats_AutoscaleCoversTheSeat_CreatesUser(bool scimInviteUserOptimization)
    {
        var factory = CreateFactory(scimInviteUserOptimization, providerClientSeatAutoscale: true);
        SeedProviderClient(factory, seatMinimum: 10, autoscaleEnabled: true);

        var context = await factory.UsersPostAsync(ScimApplicationFactory.TestOrganizationId1, NewUser());

        Assert.Equal(StatusCodes.Status201Created, context.Response.StatusCode);
        Assert.Equal(OccupiedSeats + 1, GetClientSeats(factory));
        var databaseContext = factory.GetDatabaseContext();
        Assert.Equal(OccupiedSeats + 1, databaseContext.ProviderPlans.Single().AllocatedSeats);
        Assert.False(databaseContext.Organizations.Single(o => o.Id == ScimApplicationFactory.TestOrganizationId1).SyncSeats);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Post_ProviderClientOutOfSeats_SeatMinimumFullyAssigned_ReturnsProviderMessage(
        bool scimInviteUserOptimization)
    {
        var factory = CreateFactory(scimInviteUserOptimization, providerClientSeatAutoscale: true);
        SeedProviderClient(factory, seatMinimum: OccupiedSeats, autoscaleEnabled: true);

        var context = await factory.UsersPostAsync(ScimApplicationFactory.TestOrganizationId1, NewUser());

        await AssertScimErrorAsync(context, StatusCodes.Status400BadRequest,
            ProviderClientSeatAutoscaleResult.SeatLimitReachedMessage);
        Assert.Equal(OccupiedSeats, GetClientSeats(factory));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Post_ProviderClientOutOfSeats_AutoscaleNotEnabled_ReturnsProviderMessage(
        bool scimInviteUserOptimization)
    {
        var factory = CreateFactory(scimInviteUserOptimization, providerClientSeatAutoscale: true);
        SeedProviderClient(factory, seatMinimum: 10, autoscaleEnabled: false);

        var context = await factory.UsersPostAsync(ScimApplicationFactory.TestOrganizationId1, NewUser());

        await AssertScimErrorAsync(context, StatusCodes.Status400BadRequest,
            ProviderClientSeatAutoscaleResult.SeatLimitReachedMessage);
        Assert.Equal(OccupiedSeats, GetClientSeats(factory));
    }

    private static ScimApplicationFactory CreateFactory(bool scimInviteUserOptimization,
        bool providerClientSeatAutoscale)
    {
        var factory = new ScimApplicationFactory();
        factory.SubstituteService((IFeatureService featureService) =>
        {
            featureService.IsEnabled(FeatureFlagKeys.ScimInviteUserOptimization).Returns(scimInviteUserOptimization);
            featureService.IsEnabled(FeatureFlagKeys.PM18793_ProviderClientSeatAutoscale)
                .Returns(providerClientSeatAutoscale);
        });
        factory.SubstituteService((IPricingClient pricingClient) =>
        {
            pricingClient.GetPlan(PlanType.TeamsMonthly).Returns(new TeamsMonthlyPlan());
            pricingClient.GetPlanOrThrow(PlanType.TeamsMonthly).Returns(new TeamsMonthlyPlan());
        });
        factory.ReinitializeDbForTests(factory.GetDatabaseContext());
        return factory;
    }

    private static void SeedProviderClient(ScimApplicationFactory factory, int seatMinimum, bool autoscaleEnabled)
    {
        var databaseContext = factory.GetDatabaseContext();

        var organization = databaseContext.Organizations.Single(o => o.Id == ScimApplicationFactory.TestOrganizationId1);
        organization.Status = OrganizationStatusType.Managed;
        organization.PlanType = PlanType.TeamsMonthly;
        organization.Plan = TeamsMonthlyPlan.PlanName;
        organization.Seats = OccupiedSeats;

        var provider = new Provider
        {
            Id = Guid.NewGuid(),
            Name = "Test MSP",
            BillingEmail = "billing@example.com",
            Type = ProviderType.Msp,
            Status = ProviderStatusType.Billable,
            Enabled = true
        };
        databaseContext.Providers.Add(provider);
        databaseContext.ProviderPlans.Add(new ProviderPlan
        {
            Id = Guid.NewGuid(),
            ProviderId = provider.Id,
            PlanType = PlanType.TeamsMonthly,
            SeatMinimum = seatMinimum,
            PurchasedSeats = 0,
            AllocatedSeats = OccupiedSeats
        });
        databaseContext.ProviderOrganizations.Add(new ProviderOrganization
        {
            Id = Guid.NewGuid(),
            ProviderId = provider.Id,
            OrganizationId = organization.Id,
            AutoscaleEnabled = autoscaleEnabled
        });

        databaseContext.SaveChanges();
    }

    private static int? GetClientSeats(ScimApplicationFactory factory) =>
        factory.GetDatabaseContext().Organizations
            .Single(o => o.Id == ScimApplicationFactory.TestOrganizationId1).Seats;

    private static ScimUserRequestModel NewUser() => new()
    {
        Name = new BaseScimUserModel.NameModel("New User"),
        DisplayName = "New User",
        Emails = [new BaseScimUserModel.EmailModel("new-user@example.com")],
        ExternalId = "NEW",
        Active = true,
        Schemas = [ScimConstants.Scim2SchemaUser]
    };

    private static async Task AssertScimErrorAsync(HttpContext context, int expectedStatus, string expectedDetail)
    {
        Assert.Equal(expectedStatus, context.Response.StatusCode);
        var error = await JsonSerializer.DeserializeAsync<ScimErrorResponseModel>(context.Response.Body,
            new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        Assert.Equal(expectedDetail, error!.Detail);
        Assert.Equal(expectedStatus, error.Status);
    }

    private record TeamsMonthlyPlan : Plan
    {
        public const string PlanName = "Teams (Monthly)";

        public TeamsMonthlyPlan()
        {
            Type = PlanType.TeamsMonthly;
            ProductTier = ProductTierType.Teams;
            Name = PlanName;
            HasGroups = true;
            HasDirectory = true;
            HasScim = true;
            PasswordManager = new TeamsPasswordManagerFeatures();
            SecretsManager = new TeamsSecretsManagerFeatures();
        }

        private record TeamsPasswordManagerFeatures : PasswordManagerPlanFeatures
        {
            public TeamsPasswordManagerFeatures()
            {
                BaseSeats = 0;
                HasAdditionalSeatsOption = true;
                AllowSeatAutoscale = true;
                StripeSeatPlanId = "teams-org-seat-monthly";
            }
        }

        private record TeamsSecretsManagerFeatures : SecretsManagerPlanFeatures
        {
            public TeamsSecretsManagerFeatures()
            {
                BaseSeats = 0;
                HasAdditionalSeatsOption = true;
                AllowSeatAutoscale = true;
            }
        }
    }
}
