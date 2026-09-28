using Bit.Core.AdminConsole.Entities.Provider;
using Bit.Core.AdminConsole.Providers.ProviderApiKeys.Interfaces;
using Bit.Core.AdminConsole.Repositories;
using Bit.Core.Utilities;

namespace Bit.Core.AdminConsole.Providers.ProviderApiKeys;

public class RotateProviderApiKeyCommand : IRotateProviderApiKeyCommand
{
    private readonly IProviderApiKeyRepository _providerApiKeyRepository;

    public RotateProviderApiKeyCommand(IProviderApiKeyRepository providerApiKeyRepository)
    {
        _providerApiKeyRepository = providerApiKeyRepository;
    }

    public async Task<ProviderApiKey> RotateApiKeyAsync(ProviderApiKey providerApiKey)
    {
        providerApiKey.ApiKey = CoreHelpers.SecureRandomString(30);
        providerApiKey.RevisionDate = DateTime.UtcNow;
        await _providerApiKeyRepository.UpsertAsync(providerApiKey);
        return providerApiKey;
    }
}
