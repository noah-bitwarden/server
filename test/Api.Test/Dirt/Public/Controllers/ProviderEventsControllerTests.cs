using System.Net;
using System.Reflection;
using Bit.Api.Dirt.Public.Controllers;
using Bit.Api.Dirt.Public.Models;
using Bit.Api.Models.Public.Response;
using Bit.Core;
using Bit.Core.AdminConsole.Entities.Provider;
using Bit.Core.AdminConsole.Enums.Provider;
using Bit.Core.AdminConsole.Repositories;
using Bit.Core.Context;
using Bit.Core.Dirt.Events.ProviderClientEvents;
using Bit.Core.Dirt.Events.ProviderClientEvents.Interfaces;
using Bit.Core.Enums;
using Bit.Core.Exceptions;
using Bit.Core.Models.Data;
using Bit.Test.Common.AutoFixture;
using Bit.Test.Common.AutoFixture.Attributes;
using Bitwarden.Server.Sdk.Features;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;

namespace Bit.Api.Test.Dirt.Public.Controllers;

[ControllerCustomize(typeof(ProviderEventsController))]
[SutProviderCustomize]
public class ProviderEventsControllerTests
{
    [Fact]
    public void Controller_RequiresProviderClientEventsFeatureFlag()
    {
        var attribute = typeof(ProviderEventsController).GetCustomAttribute<RequireFeatureAttribute>();

        Assert.NotNull(attribute);
        Assert.Contains(FeatureFlagKeys.ProviderClientEvents, attribute.ToString());
    }

    [Theory, BitAutoData]
    public async Task GetEvents_MapsRequestToOrgMajorQuery(SutProvider<ProviderEventsController> sutProvider)
    {
        var provider = SetupProvider(sutProvider);
        var start = new DateTime(2026, 8, 1, 6, 30, 0, DateTimeKind.Utc);
        var end = new DateTime(2026, 9, 1, 18, 45, 0, DateTimeKind.Utc);
        var query = sutProvider.GetDependency<IGetProviderClientEventsQuery>();
        query.GetOrgMajorPageAsync(Arg.Any<ProviderClientEventsRequest>())
            .Returns(new ProviderClientEventsPage([], "next"));

        var result = await sutProvider.Sut.GetEvents(
            new ProviderEventFilterRequestModel { Start = start, End = end });

        var response = GetResponse(result);
        Assert.Equal("next", response.ContinuationToken);
        await query.Received(1).GetOrgMajorPageAsync(Arg.Is<ProviderClientEventsRequest>(r =>
            r.ProviderId == provider.Id && r.Start == start && r.End == end &&
            r.OrganizationIds == null && r.ContinuationToken == null));
    }

    [Theory, BitAutoData]
    public async Task GetEvents_MapsEventsIncludingOrganizationAndProvider(
        SutProvider<ProviderEventsController> sutProvider, Guid organizationId, Guid actingUserId, Guid cipherId)
    {
        var provider = SetupProvider(sutProvider);
        var date = new DateTime(2026, 9, 1, 12, 0, 0, DateTimeKind.Utc);
        var providerEvent = new EventMessage
        {
            Type = EventType.Cipher_Updated,
            OrganizationId = organizationId,
            ProviderId = provider.Id,
            ActingUserId = actingUserId,
            CipherId = cipherId,
            Date = date,
            DeviceType = DeviceType.ChromeBrowser,
            IpAddress = "192.0.2.1",
        };
        var memberEvent = new EventMessage { Type = EventType.User_LoggedIn, OrganizationId = organizationId, Date = date };
        sutProvider.GetDependency<IGetProviderClientEventsQuery>()
            .GetOrgMajorPageAsync(Arg.Any<ProviderClientEventsRequest>())
            .Returns(new ProviderClientEventsPage([providerEvent, memberEvent], null));

        var response = GetResponse(await sutProvider.Sut.GetEvents(new ProviderEventFilterRequestModel()));

        Assert.Null(response.ContinuationToken);
        Assert.Collection(response.Data,
            e =>
            {
                Assert.Equal("event", e.Object);
                Assert.Equal(EventType.Cipher_Updated, e.Type);
                Assert.Equal(organizationId, e.OrganizationId);
                Assert.Equal(provider.Id, e.ProviderId);
                Assert.Equal(actingUserId, e.ActingUserId);
                Assert.Equal(cipherId, e.ItemId);
                Assert.Equal(date, e.Date);
                Assert.Equal(DeviceType.ChromeBrowser, e.Device);
                Assert.Equal("192.0.2.1", e.IpAddress);
            },
            e =>
            {
                Assert.Equal(organizationId, e.OrganizationId);
                Assert.Null(e.ProviderId);
            });
    }

    [Theory, BitAutoData]
    public async Task GetClientEvents_MapsRequestToSingleOrgQuery(
        SutProvider<ProviderEventsController> sutProvider, Guid organizationId)
    {
        var provider = SetupProvider(sutProvider);
        var query = sutProvider.GetDependency<IGetProviderClientEventsQuery>();
        query.GetSingleOrgPageAsync(Arg.Any<ProviderClientEventsRequest>(), organizationId)
            .Returns(new ProviderClientEventsPage(
                [new EventMessage { OrganizationId = organizationId, Date = DateTime.UtcNow }], "next"));

        var response = GetResponse(await sutProvider.Sut.GetClientEvents(organizationId,
            new ProviderEventFilterRequestModel { ContinuationToken = "token" }));

        Assert.Equal("next", response.ContinuationToken);
        Assert.Equal(organizationId, Assert.Single(response.Data).OrganizationId);
        await query.Received(1).GetSingleOrgPageAsync(
            Arg.Is<ProviderClientEventsRequest>(r => r.ProviderId == provider.Id && r.ContinuationToken == "token"),
            organizationId);
    }

    [Theory, BitAutoData]
    public async Task GetClientEvents_UnlinkedOrganization_ThrowsNotFound(
        SutProvider<ProviderEventsController> sutProvider, Guid organizationId)
    {
        SetupProvider(sutProvider);
        sutProvider.GetDependency<IGetProviderClientEventsQuery>()
            .GetSingleOrgPageAsync(Arg.Any<ProviderClientEventsRequest>(), organizationId)
            .ThrowsAsync(new NotFoundException());

        await Assert.ThrowsAsync<NotFoundException>(() =>
            sutProvider.Sut.GetClientEvents(organizationId, new ProviderEventFilterRequestModel()));
    }

    [Theory, BitAutoData]
    public async Task DateRangeOver367Days_ThrowsBadRequest(
        SutProvider<ProviderEventsController> sutProvider, Guid organizationId)
    {
        SetupProvider(sutProvider);
        var request = new ProviderEventFilterRequestModel
        {
            Start = DateTime.UtcNow.AddDays(-400),
            End = DateTime.UtcNow,
        };

        await Assert.ThrowsAsync<BadRequestException>(() => sutProvider.Sut.GetEvents(request));
        await Assert.ThrowsAsync<BadRequestException>(() => sutProvider.Sut.GetClientEvents(organizationId, request));
    }

    [Theory, BitAutoData]
    public async Task NoProviderIdInToken_ReturnsUnauthorized(
        SutProvider<ProviderEventsController> sutProvider, Guid organizationId)
    {
        sutProvider.GetDependency<ICurrentContext>().ProviderId.Returns((Guid?)null);

        Assert.IsType<UnauthorizedResult>(await sutProvider.Sut.GetEvents(new ProviderEventFilterRequestModel()));
        Assert.IsType<UnauthorizedResult>(
            await sutProvider.Sut.GetClientEvents(organizationId, new ProviderEventFilterRequestModel()));
        await sutProvider.GetDependency<IGetProviderClientEventsQuery>().ReceivedWithAnyArgs(0)
            .GetOrgMajorPageAsync(default!);
    }

    [Theory]
    [BitAutoData(ProviderType.BusinessUnit, true, ProviderStatusType.Billable)]
    [BitAutoData(ProviderType.Msp, false, ProviderStatusType.Billable)]
    [BitAutoData(ProviderType.Msp, true, ProviderStatusType.Created)]
    public async Task IneligibleProvider_ReturnsForbidden(ProviderType type, bool enabled, ProviderStatusType status,
        SutProvider<ProviderEventsController> sutProvider, Guid organizationId)
    {
        SetupProvider(sutProvider, type, enabled, status);

        AssertForbidden(await sutProvider.Sut.GetEvents(new ProviderEventFilterRequestModel()));
        AssertForbidden(await sutProvider.Sut.GetClientEvents(organizationId, new ProviderEventFilterRequestModel()));
        var query = sutProvider.GetDependency<IGetProviderClientEventsQuery>();
        await query.ReceivedWithAnyArgs(0).GetOrgMajorPageAsync(default!);
        await query.ReceivedWithAnyArgs(0).GetSingleOrgPageAsync(default!, default);
    }

    private static Provider SetupProvider(
        SutProvider<ProviderEventsController> sutProvider,
        ProviderType type = ProviderType.Msp,
        bool enabled = true,
        ProviderStatusType status = ProviderStatusType.Billable)
    {
        var provider = new Provider { Id = Guid.NewGuid(), Type = type, Enabled = enabled, Status = status };
        sutProvider.GetDependency<ICurrentContext>().ProviderId.Returns(provider.Id);
        sutProvider.GetDependency<IProviderRepository>().GetByIdAsync(provider.Id).Returns(provider);
        return provider;
    }

    private static PagedListResponseModel<ProviderEventResponseModel> GetResponse(IActionResult result) =>
        Assert.IsType<PagedListResponseModel<ProviderEventResponseModel>>(Assert.IsType<JsonResult>(result).Value);

    private static void AssertForbidden(IActionResult result)
    {
        var objectResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal((int)HttpStatusCode.Forbidden, objectResult.StatusCode);
        Assert.IsType<ErrorResponseModel>(objectResult.Value);
    }
}
