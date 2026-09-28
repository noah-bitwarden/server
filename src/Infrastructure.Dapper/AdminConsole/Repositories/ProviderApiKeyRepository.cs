using System.Data;
using Bit.Core.AdminConsole.Entities.Provider;
using Bit.Core.AdminConsole.Enums.Provider;
using Bit.Core.AdminConsole.Repositories;
using Bit.Core.Settings;
using Bit.Infrastructure.Dapper.Repositories;
using Dapper;
using Microsoft.Data.SqlClient;

namespace Bit.Infrastructure.Dapper.AdminConsole.Repositories;

public class ProviderApiKeyRepository : Repository<ProviderApiKey, Guid>, IProviderApiKeyRepository
{
    public ProviderApiKeyRepository(GlobalSettings globalSettings)
        : this(globalSettings.SqlServer.ConnectionString, globalSettings.SqlServer.ReadOnlyConnectionString)
    { }

    public ProviderApiKeyRepository(string connectionString, string readOnlyConnectionString)
        : base(connectionString, readOnlyConnectionString)
    { }

    public async Task<IEnumerable<ProviderApiKey>> GetManyByProviderIdTypeAsync(Guid providerId, ProviderApiKeyType? type = null)
    {
        using (var connection = new SqlConnection(ConnectionString))
        {
            return await connection.QueryAsync<ProviderApiKey>(
                "[dbo].[ProviderApiKey_ReadManyByProviderIdType]",
                new
                {
                    ProviderId = providerId,
                    Type = type,
                },
                commandType: CommandType.StoredProcedure);
        }
    }
}
