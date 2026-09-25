using Bit.Core;
using Bit.Core.AdminConsole.Enums.Provider;
using Bit.Core.AdminConsole.Repositories;
using Bit.Core.Auth.IdentityServer;
using Bit.Core.Billing.Extensions;
using Bitwarden.Server.Sdk.Features;
using Duende.IdentityModel;
using Duende.IdentityServer.Models;

namespace Bit.Identity.IdentityServer.ClientProviders;

/// <summary>
/// Issues client credentials clients for Providers (<c>provider.{providerId}</c>), authenticated with the
/// Provider's <see cref="ProviderApiKeyType.BillingReadOnly"/> API key. Mirrors <see cref="OrganizationClientProvider"/>.
/// </summary>
internal class ProviderClientProvider : IClientProvider
{
    private readonly IProviderRepository _providerRepository;
    private readonly IProviderApiKeyRepository _providerApiKeyRepository;
    private readonly IFeatureService _featureService;

    public ProviderClientProvider(
        IProviderRepository providerRepository,
        IProviderApiKeyRepository providerApiKeyRepository,
        IFeatureService featureService)
    {
        _providerRepository = providerRepository;
        _providerApiKeyRepository = providerApiKeyRepository;
        _featureService = featureService;
    }

    public async Task<Client?> GetAsync(string identifier)
    {
        if (!_featureService.IsEnabled(FeatureFlagKeys.ProviderPublicApi))
        {
            return null;
        }

        if (!Guid.TryParse(identifier, out var providerId))
        {
            return null;
        }

        var provider = await _providerRepository.GetByIdAsync(providerId);
        if (provider == null)
        {
            return null;
        }

        // NOTE: A provider may only have one api key per type, enforced by a unique index
        var providerApiKey = (await _providerApiKeyRepository
                .GetManyByProviderIdTypeAsync(provider.Id, ProviderApiKeyType.BillingReadOnly))
            .SingleOrDefault();
        if (providerApiKey == null)
        {
            return null;
        }

        return new Client
        {
            ClientId = $"provider.{provider.Id}",
            RequireClientSecret = true,
            ClientSecrets = [new Secret(providerApiKey.ApiKey.Sha256())],
            AllowedScopes = [ApiScopes.ApiProvider],
            AllowedGrantTypes = GrantTypes.ClientCredentials,
            AccessTokenLifetime = 3600 * 1,
            // Matches the eligibility rules for holding a provider API key. MSP-only access is enforced by the API.
            Enabled = provider.Enabled && provider.IsBillable(),
            Claims =
            [
                new(JwtClaimTypes.Subject, provider.Id.ToString()),
            ],
        };
    }
}
