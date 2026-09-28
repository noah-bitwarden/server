using Bit.Core.AdminConsole.Entities.Provider;
using Bit.Core.AdminConsole.Enums.Provider;
using Bit.Core.AdminConsole.Providers.ProviderApiKeys.Interfaces;
using Bit.Core.AdminConsole.Repositories;
using Bit.Core.Utilities;

namespace Bit.Core.AdminConsole.Providers.ProviderApiKeys;

public class CreateProviderApiKeyCommand : ICreateProviderApiKeyCommand
{
    private readonly IProviderApiKeyRepository _providerApiKeyRepository;

    public CreateProviderApiKeyCommand(IProviderApiKeyRepository providerApiKeyRepository)
    {
        _providerApiKeyRepository = providerApiKeyRepository;
    }

    public async Task<ProviderApiKey> CreateAsync(Guid providerId, ProviderApiKeyType providerApiKeyType)
    {
        var apiKey = new ProviderApiKey
        {
            ProviderId = providerId,
            Type = providerApiKeyType,
            ApiKey = CoreHelpers.SecureRandomString(30),
            RevisionDate = DateTime.UtcNow,
        };

        await _providerApiKeyRepository.CreateAsync(apiKey);
        return apiKey;
    }
}
