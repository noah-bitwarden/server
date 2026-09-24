using Bit.Core.AdminConsole.Entities.Provider;
using Bit.Core.AdminConsole.Enums.Provider;
using Bit.Core.AdminConsole.Providers.ProviderApiKeys;
using Bit.Core.AdminConsole.Repositories;
using Bit.Test.Common.AutoFixture;
using Bit.Test.Common.AutoFixture.Attributes;
using NSubstitute;
using Xunit;

namespace Bit.Core.Test.AdminConsole.Providers.ProviderApiKeys;

[SutProviderCustomize]
public class GetProviderApiKeyQueryTests
{
    [Theory, BitAutoData]
    public async Task GetProviderApiKeyAsync_HasOne_Returns(SutProvider<GetProviderApiKeyQuery> sutProvider,
        Guid id, Guid providerId)
    {
        sutProvider.GetDependency<IProviderApiKeyRepository>()
            .GetManyByProviderIdTypeAsync(providerId, ProviderApiKeyType.BillingReadOnly)
            .Returns(new List<ProviderApiKey>
            {
                new()
                {
                    Id = id,
                    ProviderId = providerId,
                    ApiKey = "test",
                    Type = ProviderApiKeyType.BillingReadOnly,
                    RevisionDate = DateTime.UtcNow.AddDays(-1),
                },
            });

        var apiKey = await sutProvider.Sut.GetProviderApiKeyAsync(providerId, ProviderApiKeyType.BillingReadOnly);

        Assert.NotNull(apiKey);
        Assert.Equal(id, apiKey.Id);
    }

    [Theory, BitAutoData]
    public async Task GetProviderApiKeyAsync_HasNone_ReturnsNull(SutProvider<GetProviderApiKeyQuery> sutProvider,
        Guid providerId)
    {
        sutProvider.GetDependency<IProviderApiKeyRepository>()
            .GetManyByProviderIdTypeAsync(providerId, ProviderApiKeyType.BillingReadOnly)
            .Returns(new List<ProviderApiKey>());

        var apiKey = await sutProvider.Sut.GetProviderApiKeyAsync(providerId, ProviderApiKeyType.BillingReadOnly);

        Assert.Null(apiKey);
    }

    [Theory, BitAutoData]
    public async Task GetProviderApiKeyAsync_BadType_Throws(SutProvider<GetProviderApiKeyQuery> sutProvider,
        Guid providerId)
    {
        var keyType = (ProviderApiKeyType)byte.MaxValue;

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            async () => await sutProvider.Sut.GetProviderApiKeyAsync(providerId, keyType));
    }
}
