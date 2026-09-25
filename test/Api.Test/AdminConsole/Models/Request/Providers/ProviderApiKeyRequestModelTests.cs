using System.ComponentModel.DataAnnotations;
using Bit.Api.AdminConsole.Models.Request.Providers;
using Bit.Core.AdminConsole.Enums.Provider;
using Xunit;

namespace Bit.Api.Test.AdminConsole.Models.Request.Providers;

public class ProviderApiKeyRequestModelTests
{
    [Fact]
    public void Validate_DefinedType_Valid()
    {
        var model = new ProviderApiKeyRequestModel
        {
            Type = ProviderApiKeyType.BillingReadOnly,
            MasterPasswordHash = "master-password-hash",
        };

        var results = Validate(model);

        Assert.Empty(results);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(255)]
    public void Validate_UndefinedType_Invalid(byte type)
    {
        var model = new ProviderApiKeyRequestModel
        {
            Type = (ProviderApiKeyType)type,
            MasterPasswordHash = "master-password-hash",
        };

        var results = Validate(model);

        var result = Assert.Single(results);
        Assert.Contains(nameof(ProviderApiKeyRequestModel.Type), result.MemberNames);
    }

    private static List<ValidationResult> Validate(ProviderApiKeyRequestModel model)
    {
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(model, new ValidationContext(model), results, true);
        return results;
    }
}
