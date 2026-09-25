using Bit.Core.AdminConsole.Entities.Provider;

namespace Bit.Core.AdminConsole.Providers.ProviderApiKeys.Interfaces;

public interface IRotateProviderApiKeyCommand
{
    Task<ProviderApiKey> RotateApiKeyAsync(ProviderApiKey providerApiKey);
}
