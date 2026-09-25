using Bit.Core.AdminConsole.Entities.Provider;
using Bit.Core.AdminConsole.Enums.Provider;

namespace Bit.Core.AdminConsole.Providers.ProviderApiKeys.Interfaces;

public interface IGetProviderApiKeyQuery
{
    Task<ProviderApiKey?> GetProviderApiKeyAsync(Guid providerId, ProviderApiKeyType providerApiKeyType);
}
