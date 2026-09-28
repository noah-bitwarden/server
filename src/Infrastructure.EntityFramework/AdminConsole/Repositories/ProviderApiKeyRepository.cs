using AutoMapper;
using Bit.Core.AdminConsole.Entities.Provider;
using Bit.Core.AdminConsole.Enums.Provider;
using Bit.Core.AdminConsole.Repositories;
using Bit.Infrastructure.EntityFramework.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Bit.Infrastructure.EntityFramework.AdminConsole.Repositories;

public class ProviderApiKeyRepository :
    Repository<ProviderApiKey, Models.Provider.ProviderApiKey, Guid>, IProviderApiKeyRepository
{
    public ProviderApiKeyRepository(IServiceScopeFactory serviceScopeFactory, IMapper mapper)
        : base(serviceScopeFactory, mapper, db => db.ProviderApiKeys)
    { }

    public async Task<IEnumerable<ProviderApiKey>> GetManyByProviderIdTypeAsync(Guid providerId, ProviderApiKeyType? type = null)
    {
        using (var scope = ServiceScopeFactory.CreateScope())
        {
            var dbContext = GetDatabaseContext(scope);
            var apiKeys = await dbContext.ProviderApiKeys
                .Where(k => k.ProviderId == providerId && (type == null || k.Type == type))
                .ToListAsync();
            return Mapper.Map<List<ProviderApiKey>>(apiKeys);
        }
    }
}
