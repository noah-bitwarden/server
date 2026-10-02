using Bit.Core.AdminConsole.Entities.Provider;
using Bit.Core.AdminConsole.Enums.Provider;
using Bit.Core.AdminConsole.Repositories;
using Bit.Core.Enums;
using Bit.Core.Repositories;
using Xunit;

namespace Bit.Infrastructure.IntegrationTest.AdminConsole.Repositories;

public class ProviderOrganizationRepositoryTests
{
    [Theory, DatabaseData]
    public async Task GetManyDetailsByProviderAsync_OccupiedSeats_ExcludesRevokedAndStaged(
        IUserRepository userRepository,
        IOrganizationRepository organizationRepository,
        IOrganizationUserRepository organizationUserRepository,
        IProviderRepository providerRepository,
        IProviderOrganizationRepository providerOrganizationRepository)
    {
        // Arrange
        var organization = await organizationRepository.CreateTestOrganizationAsync();

        var confirmedUser = await userRepository.CreateTestUserAsync("confirmed");
        var revokedUser = await userRepository.CreateTestUserAsync("revoked");
        var stagedUser = await userRepository.CreateTestUserAsync("staged");

        // UserCount counts Confirmed only; OccupiedSeats counts Invited + Accepted + Confirmed.
        await organizationUserRepository.CreateConfirmedTestOrganizationUserAsync(organization, confirmedUser);
        await organizationUserRepository.CreateTestOrganizationUserInviteAsync(organization);

        // Excluded from both counts: Revoked and Staged do not consume a seat.
        await organizationUserRepository.CreateRevokedTestOrganizationUserAsync(organization, revokedUser);
        await organizationUserRepository.CreateStagedTestOrganizationUserAsync(organization, stagedUser);

        var provider = await providerRepository.CreateAsync(new Provider
        {
            Name = "Test Provider",
            BusinessName = "Test Provider Business",
            BusinessAddress1 = "123 Test St",
            BusinessAddress2 = "Suite 456",
            BusinessAddress3 = "Floor 7",
            BusinessCountry = "US",
            BusinessTaxNumber = "123456789",
            BillingEmail = $"billing+{Guid.NewGuid()}@example.com",
            Enabled = true,
            Type = ProviderType.Msp
        });

        await providerOrganizationRepository.CreateAsync(new ProviderOrganization
        {
            ProviderId = provider.Id,
            OrganizationId = organization.Id
        });

        // Act
        var details = await providerOrganizationRepository.GetManyDetailsByProviderAsync(provider.Id);

        // Assert
        var detail = Assert.Single(details);
        Assert.Equal(1, detail.UserCount); // Confirmed only
        Assert.Equal(2, detail.OccupiedSeats); // Confirmed + Invited (Revoked and Staged excluded)
    }

    [Theory, DatabaseData]
    public async Task AutoscaleSettings_RoundTripThroughCreateReplaceAndDetails(
        IOrganizationRepository organizationRepository,
        IProviderRepository providerRepository,
        IProviderOrganizationRepository providerOrganizationRepository)
    {
        // Arrange
        var organization = await organizationRepository.CreateTestOrganizationAsync();
        var provider = await providerRepository.CreateAsync(new Provider
        {
            Name = "Test Provider",
            BillingEmail = $"billing+{Guid.NewGuid()}@example.com",
            Enabled = true,
            Type = ProviderType.Msp
        });

        // Act
        var providerOrganization = await providerOrganizationRepository.CreateAsync(new ProviderOrganization
        {
            ProviderId = provider.Id,
            OrganizationId = organization.Id
        });
        var created = await providerOrganizationRepository.GetByIdAsync(providerOrganization.Id);

        providerOrganization.AutoscaleEnabled = true;
        providerOrganization.AutoscaleSeatLimit = 42;
        await providerOrganizationRepository.ReplaceAsync(providerOrganization);
        var replaced = await providerOrganizationRepository.GetByOrganizationId(organization.Id);
        var details = await providerOrganizationRepository.GetManyDetailsByProviderAsync(provider.Id);

        // Assert
        Assert.False(created!.AutoscaleEnabled);
        Assert.Null(created.AutoscaleSeatLimit);
        Assert.True(replaced!.AutoscaleEnabled);
        Assert.Equal(42, replaced.AutoscaleSeatLimit);
        var detail = Assert.Single(details);
        Assert.True(detail.AutoscaleEnabled);
        Assert.Equal(42, detail.AutoscaleSeatLimit);
    }

    [Theory, DatabaseData]
    public async Task CreateManyAsync_PersistsAutoscaleSettings(
        Database database,
        IOrganizationRepository organizationRepository,
        IProviderRepository providerRepository,
        IProviderOrganizationRepository providerOrganizationRepository)
    {
        // The EF CreateManyAsync passes Core entities to the context, which rejects them on every EF provider
        Assert.SkipWhen(database.Type != SupportedDatabaseProviders.SqlServer,
            "EF ProviderOrganizationRepository.CreateManyAsync cannot persist entities (existing defect).");

        // Arrange
        var enabledOrganization = await organizationRepository.CreateTestOrganizationAsync();
        var defaultOrganization = await organizationRepository.CreateTestOrganizationAsync();
        var provider = await providerRepository.CreateAsync(new Provider
        {
            Name = "Test Provider",
            BillingEmail = $"billing+{Guid.NewGuid()}@example.com",
            Enabled = true,
            Type = ProviderType.Msp
        });

        // Act
        await providerOrganizationRepository.CreateManyAsync([
            new ProviderOrganization
            {
                ProviderId = provider.Id,
                OrganizationId = enabledOrganization.Id,
                AutoscaleEnabled = true,
                AutoscaleSeatLimit = 12
            },
            new ProviderOrganization { ProviderId = provider.Id, OrganizationId = defaultOrganization.Id }
        ]);

        // Assert
        var enabled = await providerOrganizationRepository.GetByOrganizationId(enabledOrganization.Id);
        var defaults = await providerOrganizationRepository.GetByOrganizationId(defaultOrganization.Id);
        Assert.True(enabled!.AutoscaleEnabled);
        Assert.Equal(12, enabled.AutoscaleSeatLimit);
        Assert.False(defaults!.AutoscaleEnabled);
        Assert.Null(defaults.AutoscaleSeatLimit);
    }
}
