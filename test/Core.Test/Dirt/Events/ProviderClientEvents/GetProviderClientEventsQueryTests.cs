using Bit.Core.AdminConsole.Entities;
using Bit.Core.AdminConsole.Models.Data.Provider;
using Bit.Core.AdminConsole.Repositories;
using Bit.Core.Dirt.Events.ProviderClientEvents;
using Bit.Core.Exceptions;
using Bit.Core.Repositories;
using Bit.Test.Common.AutoFixture;
using Microsoft.AspNetCore.DataProtection;
using NSubstitute;
using Xunit;
using static Bit.Core.Dirt.Events.ProviderClientEvents.ProviderClientEventsConstants;

namespace Bit.Core.Test.Dirt.Events.ProviderClientEvents;

public class GetProviderClientEventsQueryTests
{
    private static readonly DateTime _now = new(2026, 9, 28, 12, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime _start = _now.AddDays(-30);

    private readonly Guid _providerId = Guid.NewGuid();
    private readonly FakeClientEventStore _store = new();
    private readonly List<Organization> _organizations = [];
    private readonly SutProvider<GetProviderClientEventsQuery> _sutProvider;

    public GetProviderClientEventsQueryTests()
    {
        var timeProvider = Substitute.For<TimeProvider>();
        timeProvider.GetUtcNow().Returns(new DateTimeOffset(_now));

        _sutProvider = new SutProvider<GetProviderClientEventsQuery>()
            .SetDependency<IDataProtectionProvider>(new EphemeralDataProtectionProvider())
            .SetDependency(timeProvider)
            .Create();

        _store.Attach(_sutProvider.GetDependency<IEventRepository>());
        _sutProvider.GetDependency<IProviderOrganizationRepository>()
            .GetManyDetailsByProviderAsync(_providerId)
            .Returns(_ => _organizations
                .Select(o => new ProviderOrganizationOrganizationDetails { OrganizationId = o.Id })
                .Reverse()
                .ToList());
        _sutProvider.GetDependency<IOrganizationRepository>()
            .GetManyByIdsAsync(Arg.Any<IEnumerable<Guid>>())
            .Returns(ci => _organizations.Where(o => ci.Arg<IEnumerable<Guid>>().Contains(o.Id)).ToList());
        _sutProvider.GetDependency<IOrganizationRepository>()
            .GetByIdAsync(Arg.Any<Guid>())
            .Returns(ci => _organizations.SingleOrDefault(o => o.Id == ci.Arg<Guid>()));
    }

    private GetProviderClientEventsQuery Sut => _sutProvider.Sut;

    // Org derivation and filter

    [Fact]
    public async Task Merged_FilterWithUnlinkedOrganization_ThrowsBadRequest()
    {
        var linked = LinkOrganization();

        var exception = await Assert.ThrowsAsync<BadRequestException>(() =>
            Sut.GetMergedPageAsync(Request(organizationIds: [linked, Guid.NewGuid()])));

        Assert.DoesNotContain(linked.ToString(), exception.Message);
        Assert.Empty(_store.Calls);
    }

    [Fact]
    public async Task OrgMajor_OrganizationWithoutUseEvents_IsSilentlyDropped()
    {
        var withEvents = LinkOrganization();
        var withoutEvents = LinkOrganization(useEvents: false);

        var page = await Sut.GetOrgMajorPageAsync(Request());

        Assert.Null(page.ContinuationToken);
        Assert.Equal(new[] { withEvents }, _store.Calls.Select(c => c.OrganizationId));
        Assert.DoesNotContain(_store.Calls, c => c.OrganizationId == withoutEvents);
    }

    [Fact]
    public async Task Merged_FilteredOrganizationWithoutUseEvents_IsSilentlyDropped()
    {
        var withEvents = LinkOrganization();
        var withoutEvents = LinkOrganization(useEvents: false);

        var page = await Sut.GetMergedPageAsync(Request(organizationIds: [withEvents, withoutEvents]));

        Assert.Null(page.ContinuationToken);
        Assert.Equal(new[] { withEvents }, _store.Calls.Select(c => c.OrganizationId));
    }

    [Fact]
    public async Task OrgMajor_DisabledOrganization_IsIncluded()
    {
        var disabled = LinkOrganization(enabled: false);
        var events = _store.Add(disabled, Dates(3));

        var page = await Sut.GetOrgMajorPageAsync(Request());

        Assert.Equal(events, page.Data);
    }

    [Fact]
    public async Task OrgMajor_OrganizationsAreReadInGuidCompareToOrder()
    {
        var orgIds = Enumerable.Range(0, 6).Select(_ => LinkOrganization()).ToList();

        await Sut.GetOrgMajorPageAsync(Request());

        var expected = orgIds.ToList();
        expected.Sort((a, b) => a.CompareTo(b));
        Assert.Equal(expected, _store.Calls.Select(c => c.OrganizationId));
    }

    // Cursor

    [Fact]
    public async Task OrgMajor_WithToken_UsesPinnedWindowAndIgnoresRequestDates()
    {
        var org = LinkOrganization();
        _store.Add(org, Dates(PageSize + 1));
        var requestedEnd = _now.AddDays(1);

        var first = await Sut.GetOrgMajorPageAsync(Request(end: requestedEnd));
        await Sut.GetOrgMajorPageAsync(Request(token: first.ContinuationToken,
            start: _now.AddDays(-300), end: _now.AddDays(-200)));

        // The first request clamps the end to now; the second reuses the first request's window.
        Assert.All(_store.Calls, c =>
        {
            Assert.Equal(_start, c.Start);
            Assert.Equal(_now, c.End);
        });
        Assert.Equal(2, _store.Calls.Count);
    }

    [Fact]
    public async Task OrgMajor_TokenFromAnotherProvider_ThrowsBadRequest()
    {
        var org = LinkOrganization();
        _store.Add(org, Dates(PageSize + 1));
        var page = await Sut.GetOrgMajorPageAsync(Request());

        await Assert.ThrowsAsync<BadRequestException>(() => Sut.GetOrgMajorPageAsync(
            Request(token: page.ContinuationToken) with { ProviderId = Guid.NewGuid() }));
    }

    [Fact]
    public async Task Merged_ChangedFilter_ThrowsBadRequest()
    {
        var orgA = LinkOrganization();
        var orgB = LinkOrganization();
        _store.Add(orgA, Dates(PageSize + 1));
        var page = await Sut.GetMergedPageAsync(Request(organizationIds: [orgA]));

        await Assert.ThrowsAsync<BadRequestException>(() =>
            Sut.GetMergedPageAsync(Request(token: page.ContinuationToken, organizationIds: [orgA, orgB])));
    }

    [Fact]
    public async Task TamperedToken_ThrowsBadRequest()
    {
        LinkOrganization();

        await Assert.ThrowsAsync<BadRequestException>(() => Sut.GetOrgMajorPageAsync(Request(token: "tampered")));
        await Assert.ThrowsAsync<BadRequestException>(() => Sut.GetMergedPageAsync(Request(token: "tampered")));
    }

    // Single-org

    [Fact]
    public async Task SingleOrg_UnlinkedOrganization_ThrowsNotFound()
    {
        LinkOrganization();

        await Assert.ThrowsAsync<NotFoundException>(() => Sut.GetSingleOrgPageAsync(Request(), Guid.NewGuid()));
        Assert.Empty(_store.Calls);
    }

    [Fact]
    public async Task SingleOrg_OrganizationWithoutUseEvents_ReturnsEmptyPage()
    {
        var org = LinkOrganization(useEvents: false);
        _store.Add(org, Dates(3));

        var page = await Sut.GetSingleOrgPageAsync(Request(), org);

        Assert.Empty(page.Data);
        Assert.Null(page.ContinuationToken);
        Assert.Empty(_store.Calls);
    }

    [Fact]
    public async Task SingleOrg_PagesThroughInnerTokenUntilNull()
    {
        var org = LinkOrganization();
        var other = LinkOrganization();
        var events = _store.Add(org, Dates(PageSize + 10));
        _store.Add(other, Dates(5));

        var first = await Sut.GetSingleOrgPageAsync(Request(), org);
        var second = await Sut.GetSingleOrgPageAsync(Request(token: first.ContinuationToken), org);

        Assert.Equal(events.Take(PageSize), first.Data);
        Assert.NotNull(first.ContinuationToken);
        Assert.Equal(events.Skip(PageSize), second.Data);
        Assert.Null(second.ContinuationToken);
        Assert.Equal(new[] { null, $"o{PageSize}" }, _store.Calls.Select(c => c.ContinuationToken));
        Assert.All(_store.Calls, c => Assert.Equal(org, c.OrganizationId));
    }

    [Fact]
    public async Task SingleOrg_TokenFromAnotherOrganization_ThrowsBadRequest()
    {
        var orgA = LinkOrganization();
        var orgB = LinkOrganization();
        _store.Add(orgA, Dates(PageSize + 1));
        var page = await Sut.GetSingleOrgPageAsync(Request(), orgA);

        await Assert.ThrowsAsync<BadRequestException>(() =>
            Sut.GetSingleOrgPageAsync(Request(token: page.ContinuationToken), orgB));
    }

    [Fact]
    public async Task SingleOrg_TokenFromOtherModes_ThrowsBadRequest()
    {
        var org = LinkOrganization();
        _store.Add(org, Dates(PageSize + 1));
        var orgMajor = await Sut.GetOrgMajorPageAsync(Request());
        var merged = await Sut.GetMergedPageAsync(Request());

        await Assert.ThrowsAsync<BadRequestException>(() =>
            Sut.GetSingleOrgPageAsync(Request(token: orgMajor.ContinuationToken), org));
        await Assert.ThrowsAsync<BadRequestException>(() =>
            Sut.GetSingleOrgPageAsync(Request(token: merged.ContinuationToken), org));
    }

    // Org-major

    [Fact]
    public async Task OrgMajor_PagesSpanOrganizationBoundaries()
    {
        var (orgA, orgB) = TwoSortedOrganizations();
        var eventsA = _store.Add(orgA, Dates(30));
        var eventsB = _store.Add(orgB, Dates(30));

        var first = await Sut.GetOrgMajorPageAsync(Request());
        var second = await Sut.GetOrgMajorPageAsync(Request(token: first.ContinuationToken));

        Assert.Equal(eventsA.Concat(eventsB.Take(20)), first.Data);
        Assert.NotNull(first.ContinuationToken);
        Assert.Equal(eventsB.Skip(20), second.Data);
        Assert.Null(second.ContinuationToken);

        // The second org is asked only for what fits, then resumed with its own inner token.
        var calls = _store.Calls.ToList();
        Assert.Equal(new FakeClientEventStore.Call(orgA, _start, _now, null, PageSize), calls[0]);
        Assert.Equal(new FakeClientEventStore.Call(orgB, _start, _now, null, 20), calls[1]);
        Assert.Equal(new FakeClientEventStore.Call(orgB, _start, _now, "o20", PageSize), calls[2]);
    }

    [Fact]
    public async Task OrgMajor_RequestedPageSizeIsRemainingRowsAsPageFills()
    {
        var orgs = Enumerable.Range(0, 3).Select(_ => LinkOrganization()).Order().ToList();
        foreach (var org in orgs)
        {
            _store.Add(org, Dates(20));
        }

        await Sut.GetOrgMajorPageAsync(Request());

        Assert.Equal(orgs, _store.Calls.Select(c => c.OrganizationId));
        Assert.Equal(new[] { PageSize, PageSize - 20, PageSize - 40 }, _store.Calls.Select(c => c.PageSize));
    }

    [Fact]
    public async Task OrgMajor_ResumePassesReturnedInnerTokenThroughUnchanged()
    {
        var org = LinkOrganization();
        _store.Add(org, Dates(PageSize + 10));

        var first = await Sut.GetOrgMajorPageAsync(Request());
        var returnedToken = $"o{PageSize}";
        await Sut.GetOrgMajorPageAsync(Request(token: first.ContinuationToken));

        var resumed = _store.Calls.Last();
        Assert.Equal(org, resumed.OrganizationId);
        Assert.Equal(returnedToken, resumed.ContinuationToken);
    }

    [Fact]
    public async Task OrgMajor_PageFillsExactlyAcrossOrganizations_NextRequestHasNoDuplicatesOrGaps()
    {
        var orgs = Enumerable.Range(0, 3).Select(_ => LinkOrganization()).Order().ToList();
        var events = _store.Add(orgs[0], Dates(20))
            .Concat(_store.Add(orgs[1], Dates(20)))
            .Concat(_store.Add(orgs[2], Dates(25)))
            .ToList();

        var pages = await ReadAllOrgMajorPagesAsync(Request());

        Assert.Equal(new[] { PageSize, 15 }, pages.Select(p => p.Data.Count));
        Assert.Equal(events, pages.SelectMany(p => p.Data));
    }

    [Fact]
    public async Task OrgMajor_LastOrganizationUnlinkedMidTraversal_SkipsToNextOrganizationWithoutInnerToken()
    {
        var orgs = Enumerable.Range(0, 3).Select(_ => LinkOrganization()).Order().ToList();
        _store.Add(orgs[0], Dates(40));
        _store.Add(orgs[1], Dates(40));
        var eventsC = _store.Add(orgs[2], Dates(5));

        var first = await Sut.GetOrgMajorPageAsync(Request());
        _organizations.RemoveAll(o => o.Id == orgs[1]);
        var second = await Sut.GetOrgMajorPageAsync(Request(token: first.ContinuationToken));

        Assert.Equal(eventsC, second.Data);
        Assert.Null(second.ContinuationToken);
        var lastCall = _store.Calls.Last();
        Assert.Equal(orgs[2], lastCall.OrganizationId);
        Assert.Null(lastCall.ContinuationToken);
    }

    [Fact]
    public async Task OrgMajor_EmptyOrganizationsExhaustBudget_ReturnsEmptyPageWithToken()
    {
        var orgs = Enumerable.Range(0, OrgMajorQueryBudget + 5).Select(_ => LinkOrganization()).Order().ToList();
        var lastEvents = _store.Add(orgs[^1], Dates(2));

        var first = await Sut.GetOrgMajorPageAsync(Request());

        Assert.Empty(first.Data);
        Assert.NotNull(first.ContinuationToken);
        Assert.Equal(OrgMajorQueryBudget, _store.Calls.Count);

        var second = await Sut.GetOrgMajorPageAsync(Request(token: first.ContinuationToken));

        Assert.Equal(lastEvents, second.Data);
        Assert.Null(second.ContinuationToken);
        Assert.Equal(orgs, _store.Calls.Select(c => c.OrganizationId));
    }

    [Fact]
    public async Task OrgMajor_NoEligibleOrganizations_ReturnsEmptyPageWithoutToken()
    {
        LinkOrganization(useEvents: false);

        var page = await Sut.GetOrgMajorPageAsync(Request());

        Assert.Empty(page.Data);
        Assert.Null(page.ContinuationToken);
    }

    // Merged

    [Fact]
    public async Task Merged_MoreThanMaxOrganizations_ThrowsBadRequest()
    {
        var orgs = Enumerable.Range(0, MergedMaxOrgs + 1).Select(_ => LinkOrganization()).ToList();

        await Assert.ThrowsAsync<BadRequestException>(() => Sut.GetMergedPageAsync(Request()));
        await Assert.ThrowsAsync<BadRequestException>(() => Sut.GetMergedPageAsync(Request(organizationIds: orgs)));
        Assert.Empty(_store.Calls);

        var page = await Sut.GetMergedPageAsync(Request(organizationIds: orgs.Take(MergedMaxOrgs).ToList()));
        Assert.Null(page.ContinuationToken);
    }

    [Fact]
    public async Task Merged_InterleavesThreeOrganizationsAcrossPages()
    {
        var orgs = Enumerable.Range(0, 3).Select(_ => LinkOrganization()).ToList();
        var random = new Random(389);
        foreach (var org in orgs)
        {
            _store.Add(org, RandomDates(random, 120));
        }

        var pages = await ReadAllMergedPagesAsync(Request());

        AssertMergedOrder(orgs, pages);
        Assert.True(pages.Count >= 7);
    }

    [Theory]
    [InlineData(10)]
    [InlineData(3)]
    public async Task Merged_ShortPagesWithToken_KeepsOrder(int shortPageSize)
    {
        var orgs = Enumerable.Range(0, 3).Select(_ => LinkOrganization()).ToList();
        var random = new Random(shortPageSize);
        foreach (var org in orgs)
        {
            _store.Add(org, RandomDates(random, 150));
        }

        _store.SetMaxPageSize(orgs[1], shortPageSize);

        var pages = await ReadAllMergedPagesAsync(Request());

        AssertMergedOrder(orgs, pages);
        Assert.Contains(_store.Calls, c => c.OrganizationId == orgs[1] && c.ContinuationToken != null);
    }

    [Fact]
    public async Task Merged_FetchLimitReachedOnEmptyPages_NextRequestStillProgresses()
    {
        var (orgA, orgB) = TwoSortedOrganizations();
        _store.Add(orgA, Dates(20));
        _store.Add(orgB, Dates(20));
        _store.SetLeadingEmptyPages(orgA, MergedMaxFetchesPerOrg * 2 + 1);

        var first = await Sut.GetMergedPageAsync(Request());

        Assert.Empty(first.Data);
        Assert.NotNull(first.ContinuationToken);
        Assert.Equal(MergedMaxFetchesPerOrg, _store.Calls.Count(c => c.OrganizationId == orgA));

        var pages = await ReadAllMergedPagesAsync(Request(token: first.ContinuationToken));

        AssertMergedOrder([orgA, orgB], pages);
        // Each request continues from the last empty page's token rather than starting over.
        Assert.Equal(1, _store.Calls.Count(c => c.OrganizationId == orgA && c.ContinuationToken == null));
    }

    [Fact]
    public async Task Merged_EqualTimestampsAcrossOrganizations_TieBreakByOrganizationId()
    {
        var (orgA, orgB) = TwoSortedOrganizations();
        var dates = Dates(PageSize).ToList();
        var eventsA = _store.Add(orgA, dates);
        var eventsB = _store.Add(orgB, dates);

        var pages = await ReadAllMergedPagesAsync(Request());

        var data = pages.SelectMany(p => p.Data).ToList();
        Assert.Equal(eventsA.Zip(eventsB).SelectMany(pair => new[] { pair.First, pair.Second }), data);
        AssertMergedOrder([orgA, orgB], pages);
    }

    [Fact]
    public async Task Merged_OrganizationUnlinkedMidTraversal_IsDropped()
    {
        var orgs = Enumerable.Range(0, 3).Select(_ => LinkOrganization()).ToList();
        var random = new Random(7);
        foreach (var org in orgs)
        {
            _store.Add(org, RandomDates(random, 60));
        }

        var first = await Sut.GetMergedPageAsync(Request());
        _organizations.RemoveAll(o => o.Id == orgs[1]);
        var rest = await ReadAllMergedPagesAsync(Request(token: first.ContinuationToken));

        var restData = rest.SelectMany(p => p.Data).ToList();
        Assert.DoesNotContain(restData, e => e.OrganizationId == orgs[1]);
        var expected = _store.MergedOrder([orgs[0], orgs[2]]).Except(first.Data).ToList();
        Assert.Equal(expected, restData);
    }

    [Fact]
    public async Task Merged_OrganizationLinkedMidTraversal_IsIgnoredUntilNewTraversal()
    {
        var orgA = LinkOrganization();
        _store.Add(orgA, Dates(PageSize + 5));

        var first = await Sut.GetMergedPageAsync(Request());
        var orgB = LinkOrganization();
        _store.Add(orgB, Dates(5));
        var second = await Sut.GetMergedPageAsync(Request(token: first.ContinuationToken));

        Assert.Null(second.ContinuationToken);
        Assert.DoesNotContain(second.Data, e => e.OrganizationId == orgB);
    }

    [Fact]
    public async Task Merged_AllOrganizationsExhausted_ReturnsNullToken()
    {
        var (orgA, orgB) = TwoSortedOrganizations();
        _store.Add(orgA, Dates(3));
        _store.Add(orgB, Dates(4));

        var page = await Sut.GetMergedPageAsync(Request());

        Assert.Equal(7, page.Data.Count);
        Assert.Null(page.ContinuationToken);
    }

    private Guid LinkOrganization(bool useEvents = true, bool enabled = true)
    {
        var organization = new Organization { Id = Guid.NewGuid(), UseEvents = useEvents, Enabled = enabled };
        _organizations.Add(organization);
        return organization.Id;
    }

    private (Guid, Guid) TwoSortedOrganizations()
    {
        var orgs = new[] { LinkOrganization(), LinkOrganization() }.Order().ToArray();
        return (orgs[0], orgs[1]);
    }

    private ProviderClientEventsRequest Request(string? token = null, IReadOnlyCollection<Guid>? organizationIds = null,
        DateTime? start = null, DateTime? end = null) => new()
        {
            ProviderId = _providerId,
            Start = start ?? _start,
            End = end ?? _now,
            OrganizationIds = organizationIds,
            ContinuationToken = token,
        };

    private async Task<List<ProviderClientEventsPage>> ReadAllOrgMajorPagesAsync(ProviderClientEventsRequest request)
    {
        var pages = new List<ProviderClientEventsPage>();
        var page = await Sut.GetOrgMajorPageAsync(request);
        pages.Add(page);
        while (page.ContinuationToken != null)
        {
            Assert.True(pages.Count < 100, "Org-major paging did not terminate.");
            page = await Sut.GetOrgMajorPageAsync(request with { ContinuationToken = page.ContinuationToken });
            pages.Add(page);
        }

        return pages;
    }

    private async Task<List<ProviderClientEventsPage>> ReadAllMergedPagesAsync(ProviderClientEventsRequest request)
    {
        var pages = new List<ProviderClientEventsPage>();
        var page = await Sut.GetMergedPageAsync(request);
        pages.Add(page);
        while (page.ContinuationToken != null)
        {
            Assert.True(pages.Count < 100, "Merged paging did not terminate.");
            page = await Sut.GetMergedPageAsync(request with { ContinuationToken = page.ContinuationToken });
            pages.Add(page);
        }

        return pages;
    }

    private void AssertMergedOrder(IEnumerable<Guid> organizationIds, List<ProviderClientEventsPage> pages)
    {
        var data = pages.SelectMany(p => p.Data).ToList();
        Assert.All(pages, p => Assert.True(p.Data.Count <= PageSize));
        Assert.Equal(data.Count, data.Distinct(ReferenceEqualityComparer.Instance).Count());
        Assert.Equal(_store.MergedOrder(organizationIds), data);
    }

    /// <summary>Distinct dates, newest first, within the default window.</summary>
    private static IEnumerable<DateTime> Dates(int count) =>
        Enumerable.Range(1, count).Select(i => _now.AddMinutes(-i));

    /// <summary>Dates with frequent collisions, so ties across organizations are exercised.</summary>
    private static IEnumerable<DateTime> RandomDates(Random random, int count) =>
        Enumerable.Range(0, count).Select(_ => _now.AddMinutes(-random.Next(1, 400)));
}
