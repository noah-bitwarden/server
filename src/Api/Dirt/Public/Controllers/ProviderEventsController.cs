using System.Net;
using Bit.Api.Dirt.Public.Models;
using Bit.Api.Models.Public.Response;
using Bit.Core;
using Bit.Core.AdminConsole.Enums.Provider;
using Bit.Core.AdminConsole.Repositories;
using Bit.Core.Auth.Identity;
using Bit.Core.Billing.Extensions;
using Bit.Core.Context;
using Bit.Core.Dirt.Events.ProviderClientEvents;
using Bit.Core.Dirt.Events.ProviderClientEvents.Interfaces;
using Bitwarden.Server.Sdk.Features;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Bit.Api.Dirt.Public.Controllers;

/// <summary>
/// Event logs across a provider's client organizations, authenticated with the Provider API key.
/// </summary>
[Route("public/provider")]
[Authorize(Policies.Provider)]
[RequireFeature(FeatureFlagKeys.ProviderClientEvents)]
public class ProviderEventsController : Controller
{
    private readonly ICurrentContext _currentContext;
    private readonly IProviderRepository _providerRepository;
    private readonly IGetProviderClientEventsQuery _getProviderClientEventsQuery;

    public ProviderEventsController(
        ICurrentContext currentContext,
        IProviderRepository providerRepository,
        IGetProviderClientEventsQuery getProviderClientEventsQuery)
    {
        _currentContext = currentContext;
        _providerRepository = providerRepository;
        _getProviderClientEventsQuery = getProviderClientEventsQuery;
    }

    /// <summary>
    /// List client events.
    /// </summary>
    /// <remarks>
    /// Returns the event logs of all of the authenticated provider's client organizations that have event logging,
    /// paged by a continuation token. Results are ordered by organization, not globally by time: all of one
    /// client's events are returned, newest first, before the next client's. Every event includes its
    /// <c>organizationId</c>. Pages can be short, or even empty, while <c>continuationToken</c> is non-null; keep
    /// requesting pages until it is null. If no date filters are provided, returns the last 30 days of events.
    /// Providing only <c>start</c> returns events from then through the current time; providing only <c>end</c>
    /// returns the 30 days before it. A range greater than 367 days is rejected. When <c>continuationToken</c> is
    /// provided, the date range from the first page is used and <c>start</c> and <c>end</c> are ignored.
    /// Only available to Managed Service Providers (MSPs).
    /// </remarks>
    /// <param name="request">The date range filter and continuation token.</param>
    [HttpGet("events")]
    [ProducesResponseType(typeof(PagedListResponseModel<ProviderEventResponseModel>), (int)HttpStatusCode.OK)]
    [ProducesResponseType(typeof(ErrorResponseModel), (int)HttpStatusCode.BadRequest)]
    [ProducesResponseType((int)HttpStatusCode.Unauthorized)]
    [ProducesResponseType(typeof(ErrorResponseModel), (int)HttpStatusCode.Forbidden)]
    public async Task<IActionResult> GetEvents([FromQuery] ProviderEventFilterRequestModel request)
    {
        var (providerId, errorResult) = await GetAuthorizedProviderIdAsync();
        if (errorResult != null)
        {
            return errorResult;
        }

        var page = await _getProviderClientEventsQuery.GetOrgMajorPageAsync(request.ToQueryRequest(providerId));
        return ToJsonResult(page);
    }

    /// <summary>
    /// List a client's events.
    /// </summary>
    /// <remarks>
    /// Returns the event logs of one of the authenticated provider's client organizations, newest first, paged by a
    /// continuation token. A continuation token only works with the same <c>organizationId</c> it was returned for.
    /// Returns an empty list if the client organization doesn't have event logging. If no date filters are provided,
    /// returns the last 30 days of events. Providing only <c>start</c> returns events from then through the current
    /// time; providing only <c>end</c> returns the 30 days before it. A range greater than 367 days is rejected.
    /// When <c>continuationToken</c> is provided, the date range from the first page is used and <c>start</c> and
    /// <c>end</c> are ignored. Only available to Managed Service Providers (MSPs).
    /// </remarks>
    /// <param name="organizationId">The client organization's unique identifier.</param>
    /// <param name="request">The date range filter and continuation token.</param>
    [HttpGet("clients/{organizationId:guid}/events")]
    [ProducesResponseType(typeof(PagedListResponseModel<ProviderEventResponseModel>), (int)HttpStatusCode.OK)]
    [ProducesResponseType(typeof(ErrorResponseModel), (int)HttpStatusCode.BadRequest)]
    [ProducesResponseType((int)HttpStatusCode.Unauthorized)]
    [ProducesResponseType(typeof(ErrorResponseModel), (int)HttpStatusCode.Forbidden)]
    [ProducesResponseType((int)HttpStatusCode.NotFound)]
    public async Task<IActionResult> GetClientEvents(
        [FromRoute] Guid organizationId,
        [FromQuery] ProviderEventFilterRequestModel request)
    {
        var (providerId, errorResult) = await GetAuthorizedProviderIdAsync();
        if (errorResult != null)
        {
            return errorResult;
        }

        // Throws NotFoundException when the organization isn't linked to the provider.
        var page = await _getProviderClientEventsQuery.GetSingleOrgPageAsync(
            request.ToQueryRequest(providerId), organizationId);
        return ToJsonResult(page);
    }

    private static JsonResult ToJsonResult(ProviderClientEventsPage page) =>
        new(new PagedListResponseModel<ProviderEventResponseModel>(
            page.Data.Select(e => new ProviderEventResponseModel(e)), page.ContinuationToken));

    /// <summary>
    /// Returns the authenticated provider's ID if it may use this API: an enabled, billable MSP provider.
    /// The provider ID only ever comes from the access token.
    /// </summary>
    /// <remarks>Mirrors <c>Bit.Api.Billing.Public.Controllers.ProviderController</c>.</remarks>
    private async Task<(Guid ProviderId, IActionResult? ErrorResult)> GetAuthorizedProviderIdAsync()
    {
        if (!_currentContext.ProviderId.HasValue)
        {
            return (Guid.Empty, new UnauthorizedResult());
        }

        var provider = await _providerRepository.GetByIdAsync(_currentContext.ProviderId.Value);
        if (provider is not { Enabled: true, Type: ProviderType.Msp } || !provider.IsBillable())
        {
            return (Guid.Empty, new ObjectResult(
                new ErrorResponseModel("Only Managed Service Providers can access this resource."))
            {
                StatusCode = (int)HttpStatusCode.Forbidden
            });
        }

        return (provider.Id, null);
    }
}
