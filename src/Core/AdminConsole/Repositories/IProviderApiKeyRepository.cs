using Bit.Core.AdminConsole.Entities.Provider;
using Bit.Core.AdminConsole.Enums.Provider;
using Bit.Core.Repositories;

namespace Bit.Core.AdminConsole.Repositories;

public interface IProviderApiKeyRepository : IRepository<ProviderApiKey, Guid>
{
    Task<IEnumerable<ProviderApiKey>> GetManyByProviderIdTypeAsync(Guid providerId, ProviderApiKeyType? type = null);
}
