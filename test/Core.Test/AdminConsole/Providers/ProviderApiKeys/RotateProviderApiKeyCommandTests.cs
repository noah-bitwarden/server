using Bit.Core.AdminConsole.Entities.Provider;
using Bit.Core.AdminConsole.Providers.ProviderApiKeys;
using Bit.Core.AdminConsole.Repositories;
using Bit.Test.Common.AutoFixture;
using Bit.Test.Common.AutoFixture.Attributes;
using Bit.Test.Common.Helpers;
using NSubstitute;
using Xunit;

namespace Bit.Core.Test.AdminConsole.Providers.ProviderApiKeys;

[SutProviderCustomize]
public class RotateProviderApiKeyCommandTests
{
    [Theory, BitAutoData]
    public async Task RotateApiKeyAsync_RotatesKey(SutProvider<RotateProviderApiKeyCommand> sutProvider,
        ProviderApiKey providerApiKey)
    {
        var existingKey = providerApiKey.ApiKey;

        providerApiKey = await sutProvider.Sut.RotateApiKeyAsync(providerApiKey);

        Assert.NotEqual(existingKey, providerApiKey.ApiKey);
        Assert.Equal(30, providerApiKey.ApiKey.Length);
        AssertHelper.AssertRecent(providerApiKey.RevisionDate);
        await sutProvider.GetDependency<IProviderApiKeyRepository>().Received(1).UpsertAsync(providerApiKey);
    }
}
