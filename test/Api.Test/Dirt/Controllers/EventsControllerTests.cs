using System.Reflection;
using Bit.Api.Dirt.Controllers;
using Bit.Core;
using Bit.Core.AdminConsole.Context;
using Bit.Core.AdminConsole.Entities.Provider;
using Bit.Core.AdminConsole.Enums.Provider;
using Bit.Core.AdminConsole.Repositories;
using Bit.Core.Context;
using Bit.Core.Dirt.Events.ProviderClientEvents;
using Bit.Core.Dirt.Events.ProviderClientEvents.Interfaces;
using Bit.Core.Entities;
using Bit.Core.Exceptions;
using Bit.Core.Models.Data;
using Bit.Core.Repositories;
using Bit.Test.Common.AutoFixture;
using Bit.Test.Common.AutoFixture.Attributes;
using Bitwarden.Server.Sdk.Features;
using NSubstitute;
using Xunit;

namespace Bit.Api.Test.Dirt.Controllers;

[ControllerCustomize(typeof(EventsController))]
[SutProviderCustomize]
public class EventsControllerTests
{
    [Theory, BitAutoData]
    public async Task GetOrganizationUser_UserNotFound_ThrowsNotFound(
        SutProvider<EventsController> sutProvider,
        Guid orgId, Guid id)
    {
        sutProvider.GetDependency<IOrganizationUserRepository>()
            .GetByIdAsync(id).Returns((OrganizationUser)null);

        await Assert.ThrowsAsync<NotFoundException>(() =>
            sutProvider.Sut.GetOrganizationUser(orgId, id));
    }

    [Theory, BitAutoData]
    public async Task GetOrganizationUser_UserHasNoUserId_ThrowsNotFound(
        SutProvider<EventsController> sutProvider,
        Guid orgId, Guid id)
    {
        var organizationUser = new OrganizationUser { Id = id, OrganizationId = orgId, UserId = null };
        sutProvider.GetDependency<IOrganizationUserRepository>()
            .GetByIdAsync(id).Returns(organizationUser);

        await Assert.ThrowsAsync<NotFoundException>(() =>
            sutProvider.Sut.GetOrganizationUser(orgId, id));
    }

    [Theory, BitAutoData]
    public async Task GetOrganizationUser_UserBelongsToDifferentOrganization_ThrowsNotFound(
        SutProvider<EventsController> sutProvider,
        Guid orgId, Guid differentOrgId, Guid id)
    {
        var organizationUser = new OrganizationUser { Id = id, OrganizationId = differentOrgId, UserId = Guid.NewGuid() };
        sutProvider.GetDependency<IOrganizationUserRepository>()
            .GetByIdAsync(id).Returns(organizationUser);

        await Assert.ThrowsAsync<NotFoundException>(() =>
            sutProvider.Sut.GetOrganizationUser(orgId, id));
    }

    [Theory, BitAutoData]
    public async Task GetOrganizationUser_NoAccessToEventLogs_ThrowsNotFound(
        SutProvider<EventsController> sutProvider,
        Guid orgId, Guid id)
    {
        var organizationUser = new OrganizationUser { Id = id, OrganizationId = orgId, UserId = Guid.NewGuid() };
        sutProvider.GetDependency<IOrganizationUserRepository>()
            .GetByIdAsync(id).Returns(organizationUser);
        sutProvider.GetDependency<ICurrentContext>()
            .AccessEventLogs(orgId).Returns(false);

        await Assert.ThrowsAsync<NotFoundException>(() =>
            sutProvider.Sut.GetOrganizationUser(orgId, id));
    }

    [Theory, BitAutoData]
    public async Task GetOrganizationUser_ValidRequest_ReturnsEvents(
        SutProvider<EventsController> sutProvider,
        Guid orgId, Guid id)
    {
        var userId = Guid.NewGuid();
        var organizationUser = new OrganizationUser { Id = id, OrganizationId = orgId, UserId = userId };
        sutProvider.GetDependency<IOrganizationUserRepository>()
            .GetByIdAsync(id).Returns(organizationUser);
        sutProvider.GetDependency<ICurrentContext>()
            .AccessEventLogs(orgId).Returns(true);
        sutProvider.GetDependency<IEventRepository>()
            .GetManyByOrganizationActingUserAsync(orgId, userId, Arg.Any<DateTime>(), Arg.Any<DateTime>(),
                Arg.Any<PageOptions>())
            .Returns(new PagedResult<IEvent>());

        await sutProvider.Sut.GetOrganizationUser(orgId, id);

        await sutProvider.GetDependency<IEventRepository>().Received(1)
            .GetManyByOrganizationActingUserAsync(orgId, userId, Arg.Any<DateTime>(), Arg.Any<DateTime>(),
                Arg.Any<PageOptions>());
    }

    [Theory, BitAutoData]
    public async Task GetProviderUser_UserNotFound_ThrowsNotFound(
        SutProvider<EventsController> sutProvider,
        Guid providerId, Guid id)
    {
        sutProvider.GetDependency<IProviderUserRepository>()
            .GetByIdAsync(id).Returns((ProviderUser)null);

        await Assert.ThrowsAsync<NotFoundException>(() =>
            sutProvider.Sut.GetProviderUser(providerId, id));
    }

    [Theory, BitAutoData]
    public async Task GetProviderUser_UserHasNoUserId_ThrowsNotFound(
        SutProvider<EventsController> sutProvider,
        Guid providerId, Guid id)
    {
        var providerUser = new ProviderUser { Id = id, ProviderId = providerId, UserId = null };
        sutProvider.GetDependency<IProviderUserRepository>()
            .GetByIdAsync(id).Returns(providerUser);

        await Assert.ThrowsAsync<NotFoundException>(() =>
            sutProvider.Sut.GetProviderUser(providerId, id));
    }

    [Theory, BitAutoData]
    public async Task GetProviderUser_UserBelongsToDifferentProvider_ThrowsNotFound(
        SutProvider<EventsController> sutProvider,
        Guid providerId, Guid differentProviderId, Guid id)
    {
        var providerUser = new ProviderUser { Id = id, ProviderId = differentProviderId, UserId = Guid.NewGuid() };
        sutProvider.GetDependency<IProviderUserRepository>()
            .GetByIdAsync(id).Returns(providerUser);

        await Assert.ThrowsAsync<NotFoundException>(() =>
            sutProvider.Sut.GetProviderUser(providerId, id));
    }

    [Theory, BitAutoData]
    public async Task GetProviderUser_NoAccessToEventLogs_ThrowsNotFound(
        SutProvider<EventsController> sutProvider,
        Guid providerId, Guid id)
    {
        var providerUser = new ProviderUser { Id = id, ProviderId = providerId, UserId = Guid.NewGuid() };
        sutProvider.GetDependency<IProviderUserRepository>()
            .GetByIdAsync(id).Returns(providerUser);
        sutProvider.GetDependency<ICurrentContext>()
            .ProviderAccessEventLogs(providerId).Returns(false);

        await Assert.ThrowsAsync<NotFoundException>(() =>
            sutProvider.Sut.GetProviderUser(providerId, id));
    }

    [Theory, BitAutoData]
    public async Task GetProviderUser_ValidRequest_ReturnsEvents(
        SutProvider<EventsController> sutProvider,
        Guid providerId, Guid id)
    {
        var userId = Guid.NewGuid();
        var providerUser = new ProviderUser { Id = id, ProviderId = providerId, UserId = userId };
        sutProvider.GetDependency<IProviderUserRepository>()
            .GetByIdAsync(id).Returns(providerUser);
        sutProvider.GetDependency<ICurrentContext>()
            .ProviderAccessEventLogs(providerId).Returns(true);
        sutProvider.GetDependency<IEventRepository>()
            .GetManyByProviderActingUserAsync(providerId, userId, Arg.Any<DateTime>(), Arg.Any<DateTime>(),
                Arg.Any<PageOptions>())
            .Returns(new PagedResult<IEvent>());

        await sutProvider.Sut.GetProviderUser(providerId, id);

        await sutProvider.GetDependency<IEventRepository>().Received(1)
            .GetManyByProviderActingUserAsync(providerId, userId, Arg.Any<DateTime>(), Arg.Any<DateTime>(),
                Arg.Any<PageOptions>());
    }

    [Theory, BitAutoData]
    public async Task GetSend_NoAccessToEventLogs_ThrowsNotFound(
        SutProvider<EventsController> sutProvider, Guid orgId, Guid id)
    {
        sutProvider.GetDependency<ICurrentContext>().AccessEventLogs(orgId).Returns(false);

        await Assert.ThrowsAsync<NotFoundException>(() => sutProvider.Sut.GetSend(orgId, id));

        await sutProvider.GetDependency<IEventRepository>().DidNotReceiveWithAnyArgs()
            .GetManyBySendAsync(default, default, default, default, default);
    }

    [Theory, BitAutoData]
    public async Task GetSend_EmptyIds_ThrowsNotFound(SutProvider<EventsController> sutProvider, Guid orgId)
    {
        await Assert.ThrowsAsync<NotFoundException>(() => sutProvider.Sut.GetSend(Guid.Empty, Guid.NewGuid()));
        await Assert.ThrowsAsync<NotFoundException>(() => sutProvider.Sut.GetSend(orgId, Guid.Empty));
    }

    [Theory, BitAutoData]
    public async Task GetSend_ValidRequest_ReturnsEvents(
        SutProvider<EventsController> sutProvider, Guid orgId, Guid id)
    {
        sutProvider.GetDependency<ICurrentContext>().AccessEventLogs(orgId).Returns(true);
        sutProvider.GetDependency<IEventRepository>()
            .GetManyBySendAsync(orgId, id, Arg.Any<DateTime>(), Arg.Any<DateTime>(), Arg.Any<PageOptions>())
            .Returns(new PagedResult<IEvent>());

        await sutProvider.Sut.GetSend(orgId, id);

        await sutProvider.GetDependency<IEventRepository>().Received(1)
            .GetManyBySendAsync(orgId, id, Arg.Any<DateTime>(), Arg.Any<DateTime>(), Arg.Any<PageOptions>());
    }

    [Fact]
    public void GetProviderClientEvents_RequiresProviderClientEventsFeatureFlag()
    {
        var attribute = typeof(EventsController).GetMethod(nameof(EventsController.GetProviderClientEvents))!
            .GetCustomAttribute<RequireFeatureAttribute>();

        Assert.NotNull(attribute);
        Assert.Contains(FeatureFlagKeys.ProviderClientEvents, attribute.ToString());
    }

    [Theory, BitAutoData]
    public async Task GetProviderClientEvents_ServiceUser_ThrowsNotFound(
        SutProvider<EventsController> sutProvider, Guid providerId)
    {
        UseProviderMembership(sutProvider, providerId, ProviderUserType.ServiceUser);

        await Assert.ThrowsAsync<NotFoundException>(() =>
            sutProvider.Sut.GetProviderClientEvents(providerId));

        await sutProvider.GetDependency<IGetProviderClientEventsQuery>().DidNotReceiveWithAnyArgs()
            .GetMergedPageAsync(default);
    }

    [Theory]
    [BitAutoData(ProviderUserType.ProviderAdmin)]
    [BitAutoData(ProviderUserType.ServiceUser)]
    public async Task GetProviderClientEvents_UserOfAnotherProvider_ThrowsNotFound(ProviderUserType type,
        SutProvider<EventsController> sutProvider, Guid providerId, Guid otherProviderId)
    {
        UseProviderMembership(sutProvider, otherProviderId, type);

        await Assert.ThrowsAsync<NotFoundException>(() =>
            sutProvider.Sut.GetProviderClientEvents(providerId));

        await sutProvider.GetDependency<IGetProviderClientEventsQuery>().DidNotReceiveWithAnyArgs()
            .GetMergedPageAsync(default);
    }

    [Theory, BitAutoData]
    public async Task GetProviderClientEvents_ValidRequest_MapsRequestAndResponse(
        SutProvider<EventsController> sutProvider, Guid providerId, Guid orgA, Guid orgB, Guid actingUserId)
    {
        var start = new DateTime(2026, 8, 1, 6, 30, 0, DateTimeKind.Utc);
        var end = new DateTime(2026, 9, 1, 18, 45, 0, DateTimeKind.Utc);
        var ev = new EventMessage
        {
            Type = Core.Enums.EventType.Collection_Created,
            OrganizationId = orgB,
            ProviderId = providerId,
            ActingUserId = actingUserId,
            Date = end,
        };
        UseProviderMembership(sutProvider, providerId, ProviderUserType.ProviderAdmin);
        var query = sutProvider.GetDependency<IGetProviderClientEventsQuery>();
        query.GetMergedPageAsync(Arg.Any<ProviderClientEventsRequest>())
            .Returns(new ProviderClientEventsPage([ev], "next"));

        var response = await sutProvider.Sut.GetProviderClientEvents(providerId, start, end, null, [orgA, orgB]);

        Assert.Equal("next", response.ContinuationToken);
        var result = Assert.Single(response.Data);
        Assert.Equal(orgB, result.OrganizationId);
        Assert.Equal(providerId, result.ProviderId);
        Assert.Equal(actingUserId, result.ActingUserId);
        Assert.Equal(Core.Enums.EventType.Collection_Created, result.Type);
        await query.Received(1).GetMergedPageAsync(Arg.Is<ProviderClientEventsRequest>(r =>
            r.ProviderId == providerId && r.Start == start && r.End == end && r.ContinuationToken == null &&
            r.OrganizationIds.SequenceEqual(new[] { orgA, orgB })));
    }

    [Theory, BitAutoData]
    public async Task GetProviderClientEvents_WithContinuationToken_IgnoresDates(
        SutProvider<EventsController> sutProvider, Guid providerId)
    {
        UseProviderMembership(sutProvider, providerId, ProviderUserType.ProviderAdmin);
        var query = sutProvider.GetDependency<IGetProviderClientEventsQuery>();
        query.GetMergedPageAsync(Arg.Any<ProviderClientEventsRequest>())
            .Returns(new ProviderClientEventsPage([], null));

        // A range over 367 days would be rejected without a token.
        var response = await sutProvider.Sut.GetProviderClientEvents(providerId,
            DateTime.UtcNow.AddDays(-400), DateTime.UtcNow, "token");

        Assert.Null(response.ContinuationToken);
        await query.Received(1).GetMergedPageAsync(Arg.Is<ProviderClientEventsRequest>(r =>
            r.ContinuationToken == "token"));
    }

    [Theory, BitAutoData]
    public async Task GetProviderClientEvents_RangeOver367Days_ThrowsBadRequest(
        SutProvider<EventsController> sutProvider, Guid providerId)
    {
        UseProviderMembership(sutProvider, providerId, ProviderUserType.ProviderAdmin);

        await Assert.ThrowsAsync<BadRequestException>(() => sutProvider.Sut.GetProviderClientEvents(providerId,
            DateTime.UtcNow.AddDays(-400), DateTime.UtcNow));
    }

    /// <summary>
    /// Uses a real <see cref="CurrentContext"/> so the provider role check itself is exercised.
    /// </summary>
    private static void UseProviderMembership(SutProvider<EventsController> sutProvider, Guid providerId,
        ProviderUserType type)
    {
        var currentContext = new CurrentContext(Substitute.For<IProviderOrganizationRepository>(),
            Substitute.For<IProviderUserRepository>())
        {
            Providers = [new CurrentContextProvider { Id = providerId, Type = type }],
        };
        // Registered under the constructor parameter name so it replaces the mock SutProvider already created.
        sutProvider.SetDependency<ICurrentContext>(currentContext, "currentContext").Create();
    }
}
