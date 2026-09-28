using AutoMapper;

namespace Bit.Infrastructure.EntityFramework.AdminConsole.Models.Provider;

public class ProviderApiKey : Core.AdminConsole.Entities.Provider.ProviderApiKey
{
}

public class ProviderApiKeyMapperProfile : Profile
{
    public ProviderApiKeyMapperProfile()
    {
        CreateMap<Core.AdminConsole.Entities.Provider.ProviderApiKey, ProviderApiKey>().ReverseMap();
    }
}
