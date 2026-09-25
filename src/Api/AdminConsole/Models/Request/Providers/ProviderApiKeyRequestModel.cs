using System.ComponentModel.DataAnnotations;
using Bit.Api.Auth.Models.Request.Accounts;
using Bit.Core.AdminConsole.Enums.Provider;

namespace Bit.Api.AdminConsole.Models.Request.Providers;

public class ProviderApiKeyRequestModel : SecretVerificationRequestModel
{
    [EnumDataType(typeof(ProviderApiKeyType))]
    public ProviderApiKeyType Type { get; set; }
}
