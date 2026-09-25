using Bit.Core;
using Bit.Core.AdminConsole.Entities.Provider;
using Bit.Core.AdminConsole.Enums.Provider;
using Bit.Core.AdminConsole.Repositories;
using Bit.Core.Auth.IdentityServer;
using Bit.Identity.IdentityServer.ClientProviders;
using Bitwarden.Server.Sdk.Features;
using Duende.IdentityModel;
using NSubstitute;
using Xunit;

namespace Bit.Identity.Test.IdentityServer.ClientProviders;

public class ProviderClientProviderTests
{
    private readonly IProviderRepository _providerRepository;
    private readonly IProviderApiKeyRepository _providerApiKeyRepository;
    private readonly IFeatureService _featureService;
    private readonly ProviderClientProvider _sut;

    public ProviderClientProviderTests()
    {
        _providerRepository = Substitute.For<IProviderRepository>();
        _providerApiKeyRepository = Substitute.For<IProviderApiKeyRepository>();
        _featureService = Substitute.For<IFeatureService>();
        _featureService.IsEnabled(FeatureFlagKeys.ProviderPublicApi).Returns(true);

        _sut = new ProviderClientProvider(_providerRepository, _providerApiKeyRepository, _featureService);
    }

    [Fact]
    public async Task GetAsync_FeatureFlagDisabled_ReturnsNull()
    {
        var provider = SetupProvider();
        _featureService.IsEnabled(FeatureFlagKeys.ProviderPublicApi).Returns(false);

        var client = await _sut.GetAsync(provider.Id.ToString());

        Assert.Null(client);
        await _providerRepository.DidNotReceiveWithAnyArgs().GetByIdAsync(default);
    }

    [Fact]
    public async Task GetAsync_NonGuidIdentifier_ReturnsNull()
    {
        var client = await _sut.GetAsync("non-guid");

        Assert.Null(client);
    }

    [Fact]
    public async Task GetAsync_NonExistingProvider_ReturnsNull()
    {
        var client = await _sut.GetAsync(Guid.NewGuid().ToString());

        Assert.Null(client);
    }

    [Fact]
    public async Task GetAsync_ProviderWithoutApiKey_ReturnsNull()
    {
        var provider = SetupProvider(withApiKey: false);

        var client = await _sut.GetAsync(provider.Id.ToString());

        Assert.Null(client);
    }

    [Theory]
    [InlineData(true, ProviderType.Msp, ProviderStatusType.Billable, true)]
    [InlineData(true, ProviderType.BusinessUnit, ProviderStatusType.Billable, true)]
    [InlineData(false, ProviderType.Msp, ProviderStatusType.Billable, false)]
    [InlineData(true, ProviderType.Msp, ProviderStatusType.Created, false)]
    [InlineData(true, ProviderType.Reseller, ProviderStatusType.Created, false)]
    public async Task GetAsync_ExistingProvider_ReturnsClientRespectingEligibility(
        bool enabled, ProviderType type, ProviderStatusType status, bool expectedEnabled)
    {
        var provider = SetupProvider(enabled, type, status);

        var client = await _sut.GetAsync(provider.Id.ToString());

        Assert.NotNull(client);
        Assert.Equal($"provider.{provider.Id}", client.ClientId);
        Assert.True(client.RequireClientSecret);
        // The usage of this secret is tested in integration tests
        Assert.Single(client.ClientSecrets);
        Assert.Equal(ApiScopes.ApiProvider, Assert.Single(client.AllowedScopes));
        Assert.Equal(expectedEnabled, client.Enabled);
        Assert.Equal(TimeSpan.FromHours(1).TotalSeconds, client.AccessTokenLifetime);
        var claim = Assert.Single(client.Claims);
        Assert.Equal(JwtClaimTypes.Subject, claim.Type);
        Assert.Equal(provider.Id.ToString(), claim.Value);
    }

    private Provider SetupProvider(
        bool enabled = true,
        ProviderType type = ProviderType.Msp,
        ProviderStatusType status = ProviderStatusType.Billable,
        bool withApiKey = true)
    {
        var provider = new Provider { Id = Guid.NewGuid(), Enabled = enabled, Type = type, Status = status };
        _providerRepository.GetByIdAsync(provider.Id).Returns(provider);

        var apiKeys = withApiKey
            ? new[]
            {
                new ProviderApiKey
                {
                    Id = Guid.NewGuid(),
                    ProviderId = provider.Id,
                    Type = ProviderApiKeyType.BillingReadOnly,
                    ApiKey = "test-api-key",
                    RevisionDate = DateTime.UtcNow,
                },
            }
            : [];
        _providerApiKeyRepository
            .GetManyByProviderIdTypeAsync(provider.Id, ProviderApiKeyType.BillingReadOnly)
            .Returns(apiKeys);

        return provider;
    }
}
