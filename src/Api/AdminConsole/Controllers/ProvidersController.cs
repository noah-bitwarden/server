// FIXME: Update this file to be null safe and then delete the line below
#nullable disable

using Bit.Api.AdminConsole.Authorization.Providers.Requirements;
using Bit.Api.AdminConsole.Models.Request.Providers;
using Bit.Api.AdminConsole.Models.Response.Providers;
using Bit.Api.Models.Response;
using Bit.Core;
using Bit.Core.AdminConsole.Entities.Provider;
using Bit.Core.AdminConsole.Enums.Provider;
using Bit.Core.AdminConsole.Providers.ProviderApiKeys.Interfaces;
using Bit.Core.AdminConsole.Repositories;
using Bit.Core.AdminConsole.Services;
using Bit.Core.Auth.Identity;
using Bit.Core.Billing.Extensions;
using Bit.Core.Billing.Providers.Services;
using Bit.Core.Context;
using Bit.Core.Exceptions;
using Bit.Core.Services;
using Bit.Core.Settings;
using Bit.OrganizationAuthorization;
using Bitwarden.Server.Sdk.Features;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Bit.Api.AdminConsole.Controllers;

[Route("providers")]
[Authorize("Application")]
public class ProvidersController : Controller
{
    private readonly IUserService _userService;
    private readonly IProviderRepository _providerRepository;
    private readonly IProviderService _providerService;
    private readonly GlobalSettings _globalSettings;
    private readonly IProviderBillingService _providerBillingService;
    private readonly ILogger<ProvidersController> _logger;
    private readonly ICurrentContext _currentContext;
    private readonly IGetProviderApiKeyQuery _getProviderApiKeyQuery;
    private readonly ICreateProviderApiKeyCommand _createProviderApiKeyCommand;
    private readonly IRotateProviderApiKeyCommand _rotateProviderApiKeyCommand;

    public ProvidersController(IUserService userService, IProviderRepository providerRepository,
        IProviderService providerService, GlobalSettings globalSettings,
        IProviderBillingService providerBillingService, ILogger<ProvidersController> logger,
        ICurrentContext currentContext, IGetProviderApiKeyQuery getProviderApiKeyQuery,
        ICreateProviderApiKeyCommand createProviderApiKeyCommand,
        IRotateProviderApiKeyCommand rotateProviderApiKeyCommand)
    {
        _userService = userService;
        _providerRepository = providerRepository;
        _providerService = providerService;
        _globalSettings = globalSettings;
        _providerBillingService = providerBillingService;
        _logger = logger;
        _currentContext = currentContext;
        _getProviderApiKeyQuery = getProviderApiKeyQuery;
        _createProviderApiKeyCommand = createProviderApiKeyCommand;
        _rotateProviderApiKeyCommand = rotateProviderApiKeyCommand;
    }

    [HttpGet("{providerId:guid}")]
    [Authorize<ProviderUserRequirement>]
    public async Task<ProviderResponseModel> Get([FromRoute] Guid providerId)
    {
        var provider = await _providerRepository.GetByIdAsync(providerId);
        if (provider == null)
        {
            throw new NotFoundException();
        }

        return new ProviderResponseModel(provider);
    }

    [HttpPut("{providerId:guid}")]
    [Authorize<ProviderAdminRequirement>]
    public async Task<ProviderResponseModel> Put([FromRoute] Guid providerId, [FromBody] ProviderUpdateRequestModel model)
    {
        var provider = await _providerRepository.GetByIdAsync(providerId);
        if (provider == null)
        {
            throw new NotFoundException();
        }

        // Capture original values before modifications for Stripe sync
        var originalName = provider.Name;
        var originalBillingEmail = provider.BillingEmail;

        await _providerService.UpdateAsync(model.ToProvider(provider, _globalSettings));

        // Sync name/email changes to Stripe
        if (originalName != provider.Name || originalBillingEmail != provider.BillingEmail)
        {
            try
            {
                await _providerBillingService.UpdateProviderNameAndEmail(provider);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Failed to update Stripe customer for provider {ProviderId}. Database was updated successfully.",
                    provider.Id);
            }
        }

        return new ProviderResponseModel(provider);
    }

    [HttpPost("{providerId:guid}/setup")]
    [Authorize<ProviderAdminRequirement>]
    public async Task<ProviderResponseModel> Setup([FromRoute] Guid providerId, [FromBody] ProviderSetupRequestModel model)
    {
        var provider = await _providerRepository.GetByIdAsync(providerId);
        if (provider == null)
        {
            throw new NotFoundException();
        }

        var userId = _userService.GetProperUserId(User).Value;

        var paymentMethod = model.PaymentMethod.ToDomain();
        var billingAddress = model.BillingAddress.ToDomain();

        var response =
            await _providerService.CompleteSetupAsync(model.ToProvider(provider), userId, model.Token, model.Key,
                paymentMethod, billingAddress);

        return new ProviderResponseModel(response);
    }

    [HttpPost("{providerId}/delete-recover-token")]
    [AllowAnonymous]
    public async Task PostDeleteRecoverToken([FromRoute] Guid providerId, [FromBody] ProviderVerifyDeleteRecoverRequestModel model)
    {
        var provider = await _providerRepository.GetByIdAsync(providerId);
        if (provider == null)
        {
            throw new NotFoundException();
        }
        await _providerService.DeleteAsync(provider, model.Token);
    }

    [HttpDelete("{providerId}")]
    [Authorize<ProviderAdminRequirement>]
    public async Task Delete([FromRoute] Guid providerId)
    {
        var provider = await _providerRepository.GetByIdAsync(providerId);
        if (provider == null)
        {
            throw new NotFoundException();
        }

        var user = await _userService.GetUserByPrincipalAsync(User);
        if (user == null)
        {
            throw new UnauthorizedAccessException();
        }

        await _providerService.DeleteAsync(provider);
    }

    [HttpPost("{providerId:guid}/api-key")]
    [Authorize<ProviderAdminRequirement>]
    // Web vault only: the CLI and other clients must not be able to retrieve or rotate the provider API key
    [Authorize(Policies.Web)]
    [RequireFeature(FeatureFlagKeys.ProviderApiKey)]
    public async Task<ApiKeyResponseModel> ApiKey([FromRoute] Guid providerId, [FromBody] ProviderApiKeyRequestModel model)
    {
        var provider = await GetApiKeyEligibleProviderAsync(providerId);

        await VerifySecretAsync(model);

        var providerApiKey = await _getProviderApiKeyQuery.GetProviderApiKeyAsync(provider.Id, model.Type) ??
                             await _createProviderApiKeyCommand.CreateAsync(provider.Id, model.Type);

        return new ApiKeyResponseModel(providerApiKey);
    }

    [HttpPost("{providerId:guid}/rotate-api-key")]
    [Authorize<ProviderAdminRequirement>]
    // Web vault only: the CLI and other clients must not be able to retrieve or rotate the provider API key
    [Authorize(Policies.Web)]
    [RequireFeature(FeatureFlagKeys.ProviderApiKey)]
    public async Task<ApiKeyResponseModel> RotateApiKey([FromRoute] Guid providerId, [FromBody] ProviderApiKeyRequestModel model)
    {
        var provider = await GetApiKeyEligibleProviderAsync(providerId);

        await VerifySecretAsync(model);

        var providerApiKey = await _getProviderApiKeyQuery.GetProviderApiKeyAsync(provider.Id, model.Type);
        if (providerApiKey == null)
        {
            throw new NotFoundException();
        }

        await _rotateProviderApiKeyCommand.RotateApiKeyAsync(providerApiKey);
        return new ApiKeyResponseModel(providerApiKey);
    }

    /// <summary>
    /// Returns the provider if the current user is a Provider Admin of it and it is eligible to hold an API key:
    /// an enabled, billable MSP. Otherwise throws <see cref="NotFoundException"/>.
    /// </summary>
    private async Task<Provider> GetApiKeyEligibleProviderAsync(Guid providerId)
    {
        if (!_currentContext.ProviderProviderAdmin(providerId))
        {
            throw new NotFoundException();
        }

        var provider = await _providerRepository.GetByIdAsync(providerId);
        // Only MSPs can use the provider API key today, so Business Unit providers are excluded even though they are
        // billable. IsBillable additionally requires Billable status.
        if (provider is not { Enabled: true, Type: ProviderType.Msp } || !provider.IsBillable())
        {
            throw new NotFoundException();
        }

        return provider;
    }

    private async Task VerifySecretAsync(ProviderApiKeyRequestModel model)
    {
        var user = await _userService.GetUserByPrincipalAsync(User);
        if (user == null)
        {
            throw new UnauthorizedAccessException();
        }

        if (!await _userService.VerifySecretAsync(user, model.Secret))
        {
            await Task.Delay(2000);
            throw new BadRequestException("MasterPasswordHash", "Invalid password.");
        }
    }
}
