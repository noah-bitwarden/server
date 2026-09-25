using System.Net;
using Bit.Api.Billing.Public.Models;
using Bit.Api.Models.Public.Response;
using Bit.Core;
using Bit.Core.AdminConsole.Enums.Provider;
using Bit.Core.AdminConsole.Repositories;
using Bit.Core.Auth.Identity;
using Bit.Core.Billing.Extensions;
using Bit.Core.Billing.Providers.Repositories;
using Bit.Core.Context;
using Bitwarden.Server.Sdk.Features;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Bit.Api.Billing.Public.Controllers;

// VERIFY: location/ownership (Billing/Public vs AdminConsole/Public)
[Route("public/provider")]
[Authorize(Policies.Provider)]
[RequireFeature(FeatureFlagKeys.ProviderPublicApi)]
public class ProviderController : Controller
{
    private readonly ICurrentContext _currentContext;
    private readonly IProviderRepository _providerRepository;
    private readonly IProviderInvoiceItemRepository _providerInvoiceItemRepository;

    public ProviderController(
        ICurrentContext currentContext,
        IProviderRepository providerRepository,
        IProviderInvoiceItemRepository providerInvoiceItemRepository)
    {
        _currentContext = currentContext;
        _providerRepository = providerRepository;
        _providerInvoiceItemRepository = providerInvoiceItemRepository;
    }

    /// <summary>
    /// List provider invoices.
    /// </summary>
    /// <remarks>
    /// Returns the authenticated provider's invoices and their line items, including unassigned seats.
    /// Line item totals are estimates calculated when each invoice was created.
    /// Filter with optional <c>start</c> and <c>end</c> ISO 8601 dates (<c>yyyy-MM-dd</c>); both are inclusive UTC
    /// calendar days and are compared against each invoice's <c>invoiceDate</c>. If both are omitted, only the most
    /// recent invoice is returned. Only available to Managed Service Providers (MSPs).
    /// </remarks>
    /// <param name="request">The date range filter.</param>
    [HttpGet("invoices")]
    [ProducesResponseType(typeof(PagedListResponseModel<ProviderInvoiceResponseModel>), (int)HttpStatusCode.OK)]
    [ProducesResponseType(typeof(ErrorResponseModel), (int)HttpStatusCode.BadRequest)]
    [ProducesResponseType((int)HttpStatusCode.Unauthorized)]
    [ProducesResponseType(typeof(ErrorResponseModel), (int)HttpStatusCode.Forbidden)]
    public async Task<IActionResult> GetInvoices([FromQuery] ProviderInvoiceFilterRequestModel request)
    {
        var (providerId, errorResult) = await GetAuthorizedProviderIdAsync();
        if (errorResult != null)
        {
            return errorResult;
        }

        var range = request.ToDateRange();

        var invoices = ProviderInvoiceGroups
            .Select(ProviderInvoiceGroups.Build(await _providerInvoiceItemRepository.GetByProviderId(providerId)), range)
            .Select(invoice => new ProviderInvoiceResponseModel(invoice))
            .ToList();

        return new JsonResult(new PagedListResponseModel<ProviderInvoiceResponseModel>(invoices, null));
    }

    /// <summary>
    /// List a client's invoice line items.
    /// </summary>
    /// <remarks>
    /// Returns the invoice line items for one of the authenticated provider's client organizations.
    /// Never includes unassigned seats. Line item totals are estimates calculated when each invoice was created.
    /// Each line item's <c>invoiceDate</c> is the invoice-level date, identical to the one the provider invoices
    /// endpoint returns for the same invoice. Filter with optional <c>start</c> and <c>end</c> ISO 8601 dates
    /// (<c>yyyy-MM-dd</c>); both are inclusive UTC calendar days and are compared against <c>invoiceDate</c>.
    /// If both are omitted, only the client's line items from the most recent invoice that includes the client are
    /// returned. Only available to Managed Service Providers (MSPs).
    /// </remarks>
    /// <param name="organizationId">The client organization's unique identifier.</param>
    /// <param name="request">The date range filter.</param>
    [HttpGet("clients/{organizationId:guid}/invoices")]
    [ProducesResponseType(typeof(PagedListResponseModel<ProviderClientInvoiceItemResponseModel>), (int)HttpStatusCode.OK)]
    [ProducesResponseType(typeof(ErrorResponseModel), (int)HttpStatusCode.BadRequest)]
    [ProducesResponseType((int)HttpStatusCode.Unauthorized)]
    [ProducesResponseType(typeof(ErrorResponseModel), (int)HttpStatusCode.Forbidden)]
    [ProducesResponseType((int)HttpStatusCode.NotFound)]
    public async Task<IActionResult> GetClientInvoices(
        [FromRoute] Guid organizationId,
        [FromQuery] ProviderInvoiceFilterRequestModel request)
    {
        var (providerId, errorResult) = await GetAuthorizedProviderIdAsync();
        if (errorResult != null)
        {
            return errorResult;
        }

        var range = request.ToDateRange();

        // Prototype: filters in memory after reading by provider. Could be replaced with a targeted query.
        // Invoices are built from all of the provider's rows so each invoice is dated the same as on GetInvoices.
        var clientInvoices = ProviderInvoiceGroups
            .Build(await _providerInvoiceItemRepository.GetByProviderId(providerId))
            .Where(invoice => invoice.Items.Any(i => i.ClientId == organizationId))
            .ToList();

        // Do not distinguish "not your client" from "does not exist".
        if (clientInvoices.Count == 0)
        {
            return new NotFoundResult();
        }

        var results = ProviderInvoiceGroups
            .Select(clientInvoices, range)
            .SelectMany(invoice => invoice.Items
                .Where(i => i.ClientId == organizationId)
                .Select(i => new ProviderClientInvoiceItemResponseModel(i, invoice.InvoiceDate)))
            .ToList();

        return new JsonResult(new PagedListResponseModel<ProviderClientInvoiceItemResponseModel>(results, null));
    }

    /// <summary>
    /// Returns the authenticated provider's ID if it may use this API: an enabled, billable MSP provider.
    /// The provider ID only ever comes from the access token.
    /// </summary>
    private async Task<(Guid ProviderId, IActionResult? ErrorResult)> GetAuthorizedProviderIdAsync()
    {
        if (!_currentContext.ProviderId.HasValue)
        {
            return (Guid.Empty, new UnauthorizedResult());
        }

        var provider = await _providerRepository.GetByIdAsync(_currentContext.ProviderId.Value);
        if (provider is not { Enabled: true, Type: ProviderType.Msp } || !provider.IsBillable())
        {
            // VERIFY: 403 mechanism for the public API
            return (Guid.Empty, new ObjectResult(
                new ErrorResponseModel("Only Managed Service Providers can access this resource."))
            {
                StatusCode = (int)HttpStatusCode.Forbidden
            });
        }

        return (provider.Id, null);
    }
}
