using Bit.Core.AdminConsole.Entities.Provider;
using Bit.Core.AdminConsole.Enums.Provider;
using Bit.Core.AdminConsole.Providers.ProviderApiKeys.Interfaces;
using Bit.Core.AdminConsole.Repositories;

namespace Bit.Core.AdminConsole.Providers.ProviderApiKeys;

public class GetProviderApiKeyQuery : IGetProviderApiKeyQuery
{
    private readonly IProviderApiKeyRepository _providerApiKeyRepository;

    public GetProviderApiKeyQuery(IProviderApiKeyRepository providerApiKeyRepository)
    {
        _providerApiKeyRepository = providerApiKeyRepository;
    }

    public async Task<ProviderApiKey?> GetProviderApiKeyAsync(Guid providerId, ProviderApiKeyType providerApiKeyType)
    {
        if (!Enum.IsDefined(providerApiKeyType))
        {
            throw new ArgumentOutOfRangeException(nameof(providerApiKeyType), $"Invalid value for enum {nameof(ProviderApiKeyType)}");
        }

        var apiKeys = await _providerApiKeyRepository
            .GetManyByProviderIdTypeAsync(providerId, providerApiKeyType);

        // NOTE: A provider may only have one api key per type, enforced by a unique index
        return apiKeys.SingleOrDefault();
    }
}
