using System.Collections.Concurrent;
using Bit.Core.Models.Data;
using Bit.Core.Repositories;
using NSubstitute;

namespace Bit.Core.Test.Dirt.Events.ProviderClientEvents;

/// <summary>
/// An in-memory stand-in for <see cref="IEventRepository.GetManyByOrganizationAsync"/>: newest-first pages with
/// opaque continuation tokens, plus the short and empty pages Table Storage can return.
/// </summary>
internal sealed class FakeClientEventStore
{
    private readonly Dictionary<Guid, List<IEvent>> _events = [];
    private readonly Dictionary<Guid, int> _maxPageSizes = [];
    private readonly Dictionary<Guid, int> _leadingEmptyPages = [];

    public ConcurrentQueue<Call> Calls { get; } = new();

    public record Call(Guid OrganizationId, DateTime Start, DateTime End, string? ContinuationToken, int PageSize);

    public List<IEvent> Add(Guid organizationId, IEnumerable<DateTime> dates)
    {
        var events = dates.Select(date => (IEvent)new EventMessage { OrganizationId = organizationId, Date = date })
            .ToList();
        if (!_events.TryGetValue(organizationId, out var existing))
        {
            existing = _events[organizationId] = [];
        }

        existing.AddRange(events);
        // Stable, so events sharing a date keep insertion order, like a repository's secondary key.
        _events[organizationId] = existing.OrderByDescending(e => e.Date).ToList();
        return events;
    }

    /// <summary>Returns at most <paramref name="maxPageSize"/> events per page, with a token if more remain.</summary>
    public void SetMaxPageSize(Guid organizationId, int maxPageSize) => _maxPageSizes[organizationId] = maxPageSize;

    /// <summary>Returns <paramref name="count"/> empty pages, each with a token, before any events.</summary>
    public void SetLeadingEmptyPages(Guid organizationId, int count) => _leadingEmptyPages[organizationId] = count;

    public void Attach(IEventRepository eventRepository) =>
        eventRepository
            .GetManyByOrganizationAsync(Arg.Any<Guid>(), Arg.Any<DateTime>(), Arg.Any<DateTime>(), Arg.Any<PageOptions>())
            .Returns(ci => GetPage(ci.ArgAt<Guid>(0), ci.ArgAt<DateTime>(1), ci.ArgAt<DateTime>(2),
                ci.ArgAt<PageOptions>(3)));

    /// <summary>
    /// The expected merged order: date descending, then organization ID ascending, then per-organization order.
    /// </summary>
    public List<IEvent> MergedOrder(IEnumerable<Guid> organizationIds) =>
        organizationIds
            .SelectMany(id => _events.GetValueOrDefault(id, []).Select((e, index) => (Event: e, OrgId: id, index)))
            .OrderByDescending(x => x.Event.Date)
            .ThenBy(x => x.OrgId)
            .ThenBy(x => x.index)
            .Select(x => x.Event)
            .ToList();

    private Task<PagedResult<IEvent>> GetPage(Guid organizationId, DateTime start, DateTime end, PageOptions options)
    {
        Calls.Enqueue(new Call(organizationId, start, end, options.ContinuationToken, options.PageSize));
        var events = _events.GetValueOrDefault(organizationId, []);
        var leadingEmptyPages = _leadingEmptyPages.GetValueOrDefault(organizationId);

        var (emptyPagesServed, offset) = options.ContinuationToken switch
        {
            null => (0, 0),
            ['e', .. var n] => (int.Parse(n), 0),
            ['o', .. var n] => (leadingEmptyPages, int.Parse(n)),
            _ => throw new InvalidOperationException("Unknown token"),
        };

        if (emptyPagesServed < leadingEmptyPages)
        {
            var next = emptyPagesServed + 1 < leadingEmptyPages ? $"e{emptyPagesServed + 1}"
                : events.Count > 0 ? "o0" : null;
            return Task.FromResult(new PagedResult<IEvent> { ContinuationToken = next });
        }

        var size = Math.Min(options.PageSize, _maxPageSizes.GetValueOrDefault(organizationId, int.MaxValue));
        var page = events.Skip(offset).Take(size).ToList();
        var nextOffset = offset + page.Count;
        return Task.FromResult(new PagedResult<IEvent>
        {
            Data = page,
            ContinuationToken = nextOffset < events.Count ? $"o{nextOffset}" : null,
        });
    }
}
