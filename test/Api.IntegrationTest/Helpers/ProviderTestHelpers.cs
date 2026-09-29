using Bit.Api.IntegrationTest.Factories;
using Bit.Core.AdminConsole.Entities;
using Bit.Core.AdminConsole.Entities.Provider;
using Bit.Core.AdminConsole.Enums.Provider;
using Bit.Core.AdminConsole.Repositories;
using Bit.Core.Billing.Enums;
using Bit.Core.Enums;
using Bit.Core.Repositories;

namespace Bit.Api.IntegrationTest.Helpers;

public static class ProviderTestHelpers
{
    /// <summary>
    /// Creates a provider and links it to an organization.
    /// This does NOT create any provider users.
    /// </summary>
    /// <param name="factory">The API application factory</param>
    /// <param name="organizationId">The organization ID to link to the provider</param>
    /// <param name="providerType">The type of provider to create</param>
    /// <param name="providerStatus">The provider status (defaults to Created)</param>
    /// <returns>The created provider</returns>
    public static async Task<Provider> CreateProviderAndLinkToOrganizationAsync(
        ApiApplicationFactory factory,
        Guid organizationId,
        ProviderType providerType,
        ProviderStatusType providerStatus = ProviderStatusType.Created)
    {
        var providerRepository = factory.GetService<IProviderRepository>();
        var providerOrganizationRepository = factory.GetService<IProviderOrganizationRepository>();

        // Create the provider
        var provider = await providerRepository.CreateAsync(new Provider
        {
            Name = $"Test {providerType} Provider",
            BusinessName = $"Test {providerType} Provider Business",
            BillingEmail = $"provider-{providerType.ToString().ToLower()}@example.com",
            Type = providerType,
            Status = providerStatus,
            Enabled = true
        });

        // Link the provider to the organization
        await providerOrganizationRepository.CreateAsync(new ProviderOrganization
        {
            ProviderId = provider.Id,
            OrganizationId = organizationId,
            Key = "test-provider-key"
        });

        return provider;
    }

    /// <summary>
    /// Creates <paramref name="count"/> client organizations and links them all to an existing provider.
    /// </summary>
    /// <param name="factory">The API application factory</param>
    /// <param name="providerId">The provider to link the organizations to</param>
    /// <param name="count">The number of client organizations to create</param>
    /// <param name="useEvents">Whether the organizations have event logging</param>
    /// <returns>The created organizations</returns>
    public static async Task<List<Organization>> CreateAndLinkClientOrganizationsAsync(
        ApiApplicationFactory factory,
        Guid providerId,
        int count,
        bool useEvents = true)
    {
        var organizationRepository = factory.GetService<IOrganizationRepository>();
        var providerOrganizationRepository = factory.GetService<IProviderOrganizationRepository>();

        var organizations = new List<Organization>();
        for (var i = 0; i < count; i++)
        {
            var organization = await organizationRepository.CreateAsync(new Organization
            {
                Name = $"Test Client {i}",
                BillingEmail = $"client-{Guid.NewGuid()}@example.com",
                Plan = "Enterprise (Monthly)",
                PlanType = PlanType.EnterpriseMonthly,
                Status = OrganizationStatusType.Managed,
                Enabled = true,
                UseEvents = useEvents,
                Seats = 10,
            });

            await providerOrganizationRepository.CreateAsync(new ProviderOrganization
            {
                ProviderId = providerId,
                OrganizationId = organization.Id,
                Key = "test-provider-key"
            });

            organizations.Add(organization);
        }

        return organizations;
    }

    /// <summary>
    /// Creates a providerUser for a provider.
    /// </summary>
    public static async Task<ProviderUser> CreateProviderUserAsync(
        ApiApplicationFactory factory,
        Guid providerId,
        string userEmail,
        ProviderUserType providerUserType)
    {
        var userRepository = factory.GetService<IUserRepository>();
        var user = await userRepository.GetByEmailAsync(userEmail);
        if (user is null)
        {
            throw new Exception("No user found in test setup.");
        }

        var providerUserRepository = factory.GetService<IProviderUserRepository>();
        return await providerUserRepository.CreateAsync(new ProviderUser
        {
            ProviderId = providerId,
            Status = ProviderUserStatusType.Confirmed,
            UserId = user.Id,
            Key = Guid.NewGuid().ToString(),
            Type = providerUserType
        });
    }
}
