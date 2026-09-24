using System.Reflection;
using System.Security.Claims;
using Bit.Api.AdminConsole.Controllers;
using Bit.Api.AdminConsole.Models.Request.Providers;
using Bit.Core;
using Bit.Core.AdminConsole.Entities.Provider;
using Bit.Core.AdminConsole.Enums.Provider;
using Bit.Core.AdminConsole.Providers.ProviderApiKeys;
using Bit.Core.AdminConsole.Providers.ProviderApiKeys.Interfaces;
using Bit.Core.AdminConsole.Repositories;
using Bit.Core.Auth.Identity;
using Bit.Core.Context;
using Bit.Core.Entities;
using Bit.Core.Exceptions;
using Bit.Core.Services;
using Bit.Test.Common.AutoFixture;
using Bit.Test.Common.AutoFixture.Attributes;
using Microsoft.AspNetCore.Authorization;
using NSubstitute;
using NSubstitute.ReturnsExtensions;
using Xunit;
using RequireFeatureAttribute = Bitwarden.Server.Sdk.Features.RequireFeatureAttribute;
using SdkFeatureService = Bitwarden.Server.Sdk.Features.IFeatureService;

namespace Bit.Api.Test.AdminConsole.Controllers;

[ControllerCustomize(typeof(ProvidersController))]
[SutProviderCustomize]
public class ProvidersControllerTests
{
    [Theory, BitAutoData]
    public async Task ApiKey_ServiceUser_ThrowsNotFound(
        SutProvider<ProvidersController> sutProvider,
        Provider provider)
    {
        SetupEligibleProvider(sutProvider, provider);
        var currentContext = sutProvider.GetDependency<ICurrentContext>();
        currentContext.ProviderProviderAdmin(provider.Id).Returns(false);
        currentContext.ProviderUser(provider.Id).Returns(true);

        await Assert.ThrowsAsync<NotFoundException>(
            () => sutProvider.Sut.ApiKey(provider.Id, CreateModel()));

        await AssertNoKeyCreatedOrRotated(sutProvider);
    }

    [Theory, BitAutoData]
    public async Task ApiKey_NotProviderMember_ThrowsNotFound(
        SutProvider<ProvidersController> sutProvider,
        Provider provider)
    {
        SetupEligibleProvider(sutProvider, provider);
        var currentContext = sutProvider.GetDependency<ICurrentContext>();
        currentContext.ProviderProviderAdmin(provider.Id).Returns(false);
        currentContext.ProviderUser(provider.Id).Returns(false);

        await Assert.ThrowsAsync<NotFoundException>(
            () => sutProvider.Sut.ApiKey(provider.Id, CreateModel()));

        await AssertNoKeyCreatedOrRotated(sutProvider);
    }

    [Theory, BitAutoData]
    public async Task ApiKey_ProviderDoesNotExist_ThrowsNotFound(
        SutProvider<ProvidersController> sutProvider,
        Guid providerId)
    {
        sutProvider.GetDependency<ICurrentContext>().ProviderProviderAdmin(providerId).Returns(true);
        sutProvider.GetDependency<IProviderRepository>().GetByIdAsync(providerId).ReturnsNull();

        await Assert.ThrowsAsync<NotFoundException>(
            () => sutProvider.Sut.ApiKey(providerId, CreateModel()));

        await AssertNoKeyCreatedOrRotated(sutProvider);
    }

    [Theory, BitAutoData]
    public async Task ApiKey_ProviderDisabled_ThrowsNotFound(
        SutProvider<ProvidersController> sutProvider,
        Provider provider)
    {
        SetupEligibleProvider(sutProvider, provider);
        provider.Enabled = false;

        await Assert.ThrowsAsync<NotFoundException>(
            () => sutProvider.Sut.ApiKey(provider.Id, CreateModel()));

        await AssertNoKeyCreatedOrRotated(sutProvider);
    }

    [Theory, BitAutoData]
    public async Task ApiKey_ResellerProvider_ThrowsNotFound(
        SutProvider<ProvidersController> sutProvider,
        Provider provider)
    {
        SetupEligibleProvider(sutProvider, provider);
        provider.Type = ProviderType.Reseller;

        await Assert.ThrowsAsync<NotFoundException>(
            () => sutProvider.Sut.ApiKey(provider.Id, CreateModel()));

        await AssertNoKeyCreatedOrRotated(sutProvider);
    }

    [Theory]
    [BitAutoData(ProviderStatusType.Pending)]
    [BitAutoData(ProviderStatusType.Created)]
    public async Task ApiKey_ProviderNotBillable_ThrowsNotFound(
        ProviderStatusType status,
        SutProvider<ProvidersController> sutProvider,
        Provider provider)
    {
        SetupEligibleProvider(sutProvider, provider);
        provider.Status = status;

        await Assert.ThrowsAsync<NotFoundException>(
            () => sutProvider.Sut.ApiKey(provider.Id, CreateModel()));

        await AssertNoKeyCreatedOrRotated(sutProvider);
    }

    [Theory]
    [BitAutoData(ProviderType.Msp)]
    [BitAutoData(ProviderType.BusinessUnit)]
    public async Task ApiKey_EligibleProviderType_NoExistingKey_CreatesKey(
        ProviderType providerType,
        SutProvider<ProvidersController> sutProvider,
        Provider provider,
        ProviderApiKey createdApiKey,
        User user)
    {
        SetupEligibleProvider(sutProvider, provider);
        provider.Type = providerType;
        SetupSecretVerification(sutProvider, user, true);
        sutProvider.GetDependency<IGetProviderApiKeyQuery>()
            .GetProviderApiKeyAsync(provider.Id, ProviderApiKeyType.BillingReadOnly)
            .ReturnsNull();
        sutProvider.GetDependency<ICreateProviderApiKeyCommand>()
            .CreateAsync(provider.Id, ProviderApiKeyType.BillingReadOnly)
            .Returns(createdApiKey);

        var result = await sutProvider.Sut.ApiKey(provider.Id, CreateModel());

        Assert.Equal(createdApiKey.ApiKey, result.ApiKey);
        Assert.Equal(createdApiKey.RevisionDate, result.RevisionDate);
        await sutProvider.GetDependency<ICreateProviderApiKeyCommand>()
            .Received(1)
            .CreateAsync(provider.Id, ProviderApiKeyType.BillingReadOnly);
    }

    [Theory, BitAutoData]
    public async Task ApiKey_ExistingKey_ReturnsExistingKeyWithoutCreating(
        SutProvider<ProvidersController> sutProvider,
        Provider provider,
        ProviderApiKey existingApiKey,
        User user)
    {
        SetupEligibleProvider(sutProvider, provider);
        SetupSecretVerification(sutProvider, user, true);
        sutProvider.GetDependency<IGetProviderApiKeyQuery>()
            .GetProviderApiKeyAsync(provider.Id, ProviderApiKeyType.BillingReadOnly)
            .Returns(existingApiKey);

        var result = await sutProvider.Sut.ApiKey(provider.Id, CreateModel());

        Assert.Equal(existingApiKey.ApiKey, result.ApiKey);
        Assert.Equal(existingApiKey.RevisionDate, result.RevisionDate);
        await AssertNoKeyCreatedOrRotated(sutProvider);
    }

    [Theory, BitAutoData]
    public async Task ApiKey_InvalidSecret_ThrowsBadRequestAndDoesNotCreateKey(
        SutProvider<ProvidersController> sutProvider,
        Provider provider,
        User user)
    {
        SetupEligibleProvider(sutProvider, provider);
        SetupSecretVerification(sutProvider, user, false);
        sutProvider.GetDependency<IGetProviderApiKeyQuery>()
            .GetProviderApiKeyAsync(provider.Id, ProviderApiKeyType.BillingReadOnly)
            .ReturnsNull();

        await Assert.ThrowsAsync<BadRequestException>(
            () => sutProvider.Sut.ApiKey(provider.Id, CreateModel()));

        await AssertNoKeyCreatedOrRotated(sutProvider);
        await sutProvider.GetDependency<IGetProviderApiKeyQuery>()
            .DidNotReceiveWithAnyArgs()
            .GetProviderApiKeyAsync(default, default);
    }

    [Theory, BitAutoData]
    public async Task RotateApiKey_ExistingKey_ChangesKeyAndRevisionDate(
        SutProvider<ProvidersController> sutProvider,
        Provider provider,
        User user)
    {
        var providerApiKeyRepository = Substitute.For<IProviderApiKeyRepository>();
        sutProvider.SetDependency<IRotateProviderApiKeyCommand>(
            new RotateProviderApiKeyCommand(providerApiKeyRepository), "rotateProviderApiKeyCommand");
        sutProvider.Create();

        var originalRevisionDate = DateTime.UtcNow.AddDays(-1);
        var existingApiKey = new ProviderApiKey
        {
            Id = Guid.NewGuid(),
            ProviderId = provider.Id,
            Type = ProviderApiKeyType.BillingReadOnly,
            ApiKey = "original-api-key",
            RevisionDate = originalRevisionDate,
        };

        SetupEligibleProvider(sutProvider, provider);
        SetupSecretVerification(sutProvider, user, true);
        sutProvider.GetDependency<IGetProviderApiKeyQuery>()
            .GetProviderApiKeyAsync(provider.Id, ProviderApiKeyType.BillingReadOnly)
            .Returns(existingApiKey);

        var result = await sutProvider.Sut.RotateApiKey(provider.Id, CreateModel());

        Assert.NotEqual("original-api-key", result.ApiKey);
        Assert.Equal(30, result.ApiKey.Length);
        Assert.True(result.RevisionDate > originalRevisionDate);
        await providerApiKeyRepository.Received(1).UpsertAsync(Arg.Is<ProviderApiKey>(k =>
            k.Id == existingApiKey.Id && k.ApiKey == result.ApiKey));
        await sutProvider.GetDependency<ICreateProviderApiKeyCommand>()
            .DidNotReceiveWithAnyArgs()
            .CreateAsync(default, default);
    }

    [Theory, BitAutoData]
    public async Task RotateApiKey_NoExistingKey_ThrowsNotFoundAndDoesNotCreateKey(
        SutProvider<ProvidersController> sutProvider,
        Provider provider,
        User user)
    {
        SetupEligibleProvider(sutProvider, provider);
        SetupSecretVerification(sutProvider, user, true);
        sutProvider.GetDependency<IGetProviderApiKeyQuery>()
            .GetProviderApiKeyAsync(provider.Id, ProviderApiKeyType.BillingReadOnly)
            .ReturnsNull();

        await Assert.ThrowsAsync<NotFoundException>(
            () => sutProvider.Sut.RotateApiKey(provider.Id, CreateModel()));

        await AssertNoKeyCreatedOrRotated(sutProvider);
    }

    [Theory, BitAutoData]
    public async Task RotateApiKey_InvalidSecret_ThrowsBadRequestAndDoesNotRotate(
        SutProvider<ProvidersController> sutProvider,
        Provider provider,
        ProviderApiKey existingApiKey,
        User user)
    {
        SetupEligibleProvider(sutProvider, provider);
        SetupSecretVerification(sutProvider, user, false);
        sutProvider.GetDependency<IGetProviderApiKeyQuery>()
            .GetProviderApiKeyAsync(provider.Id, ProviderApiKeyType.BillingReadOnly)
            .Returns(existingApiKey);

        await Assert.ThrowsAsync<BadRequestException>(
            () => sutProvider.Sut.RotateApiKey(provider.Id, CreateModel()));

        await AssertNoKeyCreatedOrRotated(sutProvider);
    }

    [Theory, BitAutoData]
    public async Task RotateApiKey_ServiceUser_ThrowsNotFound(
        SutProvider<ProvidersController> sutProvider,
        Provider provider)
    {
        SetupEligibleProvider(sutProvider, provider);
        var currentContext = sutProvider.GetDependency<ICurrentContext>();
        currentContext.ProviderProviderAdmin(provider.Id).Returns(false);
        currentContext.ProviderUser(provider.Id).Returns(true);

        await Assert.ThrowsAsync<NotFoundException>(
            () => sutProvider.Sut.RotateApiKey(provider.Id, CreateModel()));

        await AssertNoKeyCreatedOrRotated(sutProvider);
    }

    [Theory, BitAutoData]
    public async Task RotateApiKey_ProviderDisabled_ThrowsNotFound(
        SutProvider<ProvidersController> sutProvider,
        Provider provider)
    {
        SetupEligibleProvider(sutProvider, provider);
        provider.Enabled = false;

        await Assert.ThrowsAsync<NotFoundException>(
            () => sutProvider.Sut.RotateApiKey(provider.Id, CreateModel()));

        await AssertNoKeyCreatedOrRotated(sutProvider);
    }

    [Theory, BitAutoData]
    public async Task RotateApiKey_ResellerProvider_ThrowsNotFound(
        SutProvider<ProvidersController> sutProvider,
        Provider provider)
    {
        SetupEligibleProvider(sutProvider, provider);
        provider.Type = ProviderType.Reseller;

        await Assert.ThrowsAsync<NotFoundException>(
            () => sutProvider.Sut.RotateApiKey(provider.Id, CreateModel()));

        await AssertNoKeyCreatedOrRotated(sutProvider);
    }

    [Theory]
    [InlineData(nameof(ProvidersController.ApiKey))]
    [InlineData(nameof(ProvidersController.RotateApiKey))]
    public void ApiKeyEndpoints_AreGatedByProviderApiKeyFeatureFlag(string methodName)
    {
        // The SDK feature middleware answers 404 when the check fails. Evaluate the attribute's check against a
        // feature service to confirm the endpoint is unavailable while the flag is off and gated on this flag only.
        var method = typeof(ProvidersController).GetMethod(methodName);
        Assert.NotNull(method);
        var attribute = method.GetCustomAttributes<RequireFeatureAttribute>().SingleOrDefault();
        Assert.NotNull(attribute);

        var featureService = Substitute.For<SdkFeatureService>();
        featureService.IsEnabled(Arg.Any<string>(), Arg.Any<bool>()).Returns(false);
        Assert.False(attribute.FeatureCheck(featureService));

        featureService.IsEnabled(FeatureFlagKeys.ProviderApiKey, Arg.Any<bool>()).Returns(true);
        Assert.True(attribute.FeatureCheck(featureService));
    }

    [Theory]
    [InlineData(nameof(ProvidersController.ApiKey))]
    [InlineData(nameof(ProvidersController.RotateApiKey))]
    public void ApiKeyEndpoints_AreRestrictedToWebVault(string methodName)
    {
        // The Web policy requires the web vault client_id, so CLI and other client tokens are rejected
        var method = typeof(ProvidersController).GetMethod(methodName);
        Assert.NotNull(method);
        Assert.Contains(method.GetCustomAttributes<AuthorizeAttribute>(), a => a.Policy == Policies.Web);
    }

    private static ProviderApiKeyRequestModel CreateModel() => new()
    {
        Type = ProviderApiKeyType.BillingReadOnly,
        MasterPasswordHash = "master-password-hash",
    };

    private static void SetupEligibleProvider(SutProvider<ProvidersController> sutProvider, Provider provider)
    {
        provider.Enabled = true;
        provider.Type = ProviderType.Msp;
        provider.Status = ProviderStatusType.Billable;
        sutProvider.GetDependency<ICurrentContext>().ProviderProviderAdmin(provider.Id).Returns(true);
        sutProvider.GetDependency<IProviderRepository>().GetByIdAsync(provider.Id).Returns(provider);
    }

    private static void SetupSecretVerification(SutProvider<ProvidersController> sutProvider, User user, bool isValid)
    {
        var userService = sutProvider.GetDependency<IUserService>();
        userService.GetUserByPrincipalAsync(Arg.Any<ClaimsPrincipal>()).Returns(user);
        userService.VerifySecretAsync(user, Arg.Any<string>()).Returns(isValid);
    }

    private static async Task AssertNoKeyCreatedOrRotated(SutProvider<ProvidersController> sutProvider)
    {
        await sutProvider.GetDependency<ICreateProviderApiKeyCommand>()
            .DidNotReceiveWithAnyArgs()
            .CreateAsync(default, default);
        await sutProvider.GetDependency<IRotateProviderApiKeyCommand>()
            .DidNotReceiveWithAnyArgs()
            .RotateApiKeyAsync(default!);
    }
}
