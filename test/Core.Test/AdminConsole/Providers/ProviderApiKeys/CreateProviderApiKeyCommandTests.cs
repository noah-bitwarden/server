using Bit.Core.AdminConsole.Entities.Provider;
using Bit.Core.AdminConsole.Enums.Provider;
using Bit.Core.AdminConsole.Providers.ProviderApiKeys;
using Bit.Core.AdminConsole.Repositories;
using Bit.Test.Common.AutoFixture;
using Bit.Test.Common.AutoFixture.Attributes;
using Bit.Test.Common.Helpers;
using NSubstitute;
using Xunit;

namespace Bit.Core.Test.AdminConsole.Providers.ProviderApiKeys;

[SutProviderCustomize]
public class CreateProviderApiKeyCommandTests
{
    [Theory, BitAutoData]
    public async Task CreateAsync_CreatesProviderApiKey(SutProvider<CreateProviderApiKeyCommand> sutProvider,
        Guid providerId)
    {
        var apiKey = await sutProvider.Sut.CreateAsync(providerId, ProviderApiKeyType.BillingReadOnly);

        Assert.Equal(providerId, apiKey.ProviderId);
        Assert.Equal(ProviderApiKeyType.BillingReadOnly, apiKey.Type);
        Assert.Equal(30, apiKey.ApiKey.Length);
        AssertHelper.AssertRecent(apiKey.RevisionDate);
        await sutProvider.GetDependency<IProviderApiKeyRepository>().Received(1)
            .CreateAsync(Arg.Is<ProviderApiKey>(k => k.ProviderId == providerId
                                                    && k.Type == ProviderApiKeyType.BillingReadOnly
                                                    && k.ApiKey == apiKey.ApiKey));
    }
}
