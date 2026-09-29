using Bit.Core.AdminConsole.Repositories;
using Bit.Core.Dirt.Events.ProviderClientEvents.Interfaces;
using Bit.Core.Exceptions;
using Bit.Core.Models.Data;
using Bit.Core.Repositories;
using Microsoft.AspNetCore.DataProtection;
using static Bit.Core.Dirt.Events.ProviderClientEvents.ProviderClientEventsConstants;

namespace Bit.Core.Dirt.Events.ProviderClientEvents;

public class GetProviderClientEventsQuery : IGetProviderClientEventsQuery
{
    private readonly IProviderOrganizationRepository _providerOrganizationRepository;
    private readonly IOrganizationRepository _organizationRepository;
    private readonly IEventRepository _eventRepository;
    private readonly TimeProvider _timeProvider;
    private readonly ProviderClientEventsCursorProtector _cursorProtector;

    public GetProviderClientEventsQuery(
        IProviderOrganizationRepository providerOrganizationRepository,
        IOrganizationRepository organizationRepository,
        IEventRepository eventRepository,
        IDataProtectionProvider dataProtectionProvider,
        TimeProvider timeProvider)
    {
        _providerOrganizationRepository = providerOrganizationRepository;
        _organizationRepository = organizationRepository;
        _eventRepository = eventRepository;
        _timeProvider = timeProvider;
        _cursorProtector = new ProviderClientEventsCursorProtector(dataProtectionProvider);
    }

    /// <remarks>
    /// Requests only the rows still needed and resumes from the repository's own continuation token. The feature is
    /// cloud-only, where resuming from a Table Storage token is exact; re-reading a page and skipping by index would
    /// let a late-arriving event shift the indexes and duplicate an export row.
    /// </remarks>
    public async Task<ProviderClientEventsPage> GetOrgMajorPageAsync(ProviderClientEventsRequest request)
    {
        var (cursor, window) = ReadCursor(request, ProviderClientEventsMode.OrgMajor);
        var orgIds = await GetEligibleOrganizationIdsAsync(request.ProviderId, request.OrganizationIds);

        var index = 0;
        string? inner = null;
        if (cursor?.LastOrgId is { } lastOrgId)
        {
            var lastIndex = orgIds.IndexOf(lastOrgId);
            if (lastIndex >= 0 && cursor.InnerToken != null)
            {
                index = lastIndex;
                inner = cursor.InnerToken;
            }
            else
            {
                // Also covers lastOrgId being unlinked mid-traversal: its inner token is discarded.
                index = orgIds.FindIndex(id => id.CompareTo(lastOrgId) > 0);
                if (index < 0)
                {
                    index = orgIds.Count;
                }
            }
        }

        var data = new List<IEvent>();
        var queries = 0;
        while (data.Count < PageSize && queries < OrgMajorQueryBudget && index < orgIds.Count)
        {
            var result = await _eventRepository.GetManyByOrganizationAsync(orgIds[index], window.Start, window.End,
                new PageOptions { ContinuationToken = inner, PageSize = PageSize - data.Count });
            queries++;
            data.AddRange(result.Data);

            inner = result.ContinuationToken;
            if (inner == null)
            {
                index++;
            }
        }

        if (index >= orgIds.Count)
        {
            return new ProviderClientEventsPage(data, null);
        }

        // With no inner token the next org starts fresh, so point at the org just finished.
        var nextCursor = NewCursor(request, ProviderClientEventsMode.OrgMajor, window) with
        {
            LastOrgId = inner != null ? orgIds[index] : orgIds[index - 1],
            InnerToken = inner,
        };
        return new ProviderClientEventsPage(data, _cursorProtector.Protect(nextCursor));
    }

    public async Task<ProviderClientEventsPage> GetSingleOrgPageAsync(ProviderClientEventsRequest request,
        Guid organizationId)
    {
        var linkedOrgIds = await GetLinkedOrganizationIdsAsync(request.ProviderId);
        if (!linkedOrgIds.Contains(organizationId))
        {
            throw new NotFoundException();
        }

        var (cursor, window) = ReadCursor(request, ProviderClientEventsMode.SingleOrg);
        if (cursor != null && cursor.OrgId != organizationId)
        {
            throw new BadRequestException("Invalid continuation token.");
        }

        var organization = await _organizationRepository.GetByIdAsync(organizationId);
        if (organization is not { UseEvents: true })
        {
            return ProviderClientEventsPage.Empty;
        }

        var result = await _eventRepository.GetManyByOrganizationAsync(organizationId, window.Start, window.End,
            new PageOptions { ContinuationToken = cursor?.InnerToken, PageSize = PageSize });

        if (result.ContinuationToken == null)
        {
            return new ProviderClientEventsPage(result.Data, null);
        }

        var nextCursor = NewCursor(request, ProviderClientEventsMode.SingleOrg, window) with
        {
            OrgId = organizationId,
            InnerToken = result.ContinuationToken,
        };
        return new ProviderClientEventsPage(result.Data, _cursorProtector.Protect(nextCursor));
    }

    /// <remarks>
    /// Known limitation: in the cloud, events are written asynchronously through a queue. An event that lands late, in
    /// the part of the date range a traversal has already read, is missed, as it is by the per-organization event API.
    /// It also shifts the indexes of a page being re-read, so each late event causes one duplicate row.
    /// Organizations linked (or given <c>UseEvents</c>) mid-traversal have no position in the cursor and are ignored
    /// until the caller starts a new traversal.
    /// </remarks>
    public async Task<ProviderClientEventsPage> GetMergedPageAsync(ProviderClientEventsRequest request)
    {
        var (cursor, window) = ReadCursor(request, ProviderClientEventsMode.Merged);
        var orgIds = await GetEligibleOrganizationIdsAsync(request.ProviderId, request.OrganizationIds);
        if (orgIds.Count > MergedMaxOrgs)
        {
            throw new BadRequestException($"Select at most {MergedMaxOrgs} clients.");
        }

        var positions = cursor == null
            ? orgIds.Select(id => new ProviderClientEventsPosition { OrgId = id }).ToList()
            : cursor.Positions?.Where(p => orgIds.Contains(p.OrgId)).ToList() ?? [];

        var buffers = await FetchBuffersAsync(positions.Where(p => !p.Exhausted), window);
        var (data, consumed) = MergeSafePrefix(buffers);

        var nextPositions = positions
            .Select(p => buffers.TryGetValue(p.OrgId, out var buffer) ? NextPosition(buffer, consumed[p.OrgId]) : p)
            .ToList();

        if (nextPositions.All(p => p.Exhausted))
        {
            return new ProviderClientEventsPage(data, null);
        }

        var nextCursor = NewCursor(request, ProviderClientEventsMode.Merged, window) with { Positions = nextPositions };
        return new ProviderClientEventsPage(data, _cursorProtector.Protect(nextCursor));
    }

    /// <summary>
    /// Reads the org buffers with bounded concurrency. Each buffer holds at least <see cref="PageSize"/> events
    /// unless the organization is exhausted or its fetch limit was reached.
    /// </summary>
    private async Task<Dictionary<Guid, OrgBuffer>> FetchBuffersAsync(
        IEnumerable<ProviderClientEventsPosition> positions, DateRange window)
    {
        var buffers = new Dictionary<Guid, OrgBuffer>();
        using var semaphore = new SemaphoreSlim(MergedConcurrency, MergedConcurrency);
        var tasks = positions.Select(async position =>
        {
            await semaphore.WaitAsync();
            try
            {
                return await FetchBufferAsync(position, window);
            }
            finally
            {
                semaphore.Release();
            }
        });

        foreach (var buffer in await Task.WhenAll(tasks))
        {
            buffers[buffer.OrgId] = buffer;
        }

        return buffers;
    }

    private async Task<OrgBuffer> FetchBufferAsync(ProviderClientEventsPosition position, DateRange window)
    {
        var events = new List<BufferedEvent>();
        var token = position.InnerToken;
        // Carried across page boundaries in case a re-read of the start page comes back shorter than before.
        var skip = position.Skip;
        var fetches = 0;
        do
        {
            var pageStartToken = token;
            var result = await _eventRepository.GetManyByOrganizationAsync(position.OrgId, window.Start, window.End,
                new PageOptions { ContinuationToken = pageStartToken, PageSize = PageSize });
            fetches++;

            for (var i = 0; i < result.Data.Count; i++)
            {
                if (skip > 0)
                {
                    skip--;
                    continue;
                }

                events.Add(new BufferedEvent(result.Data[i], pageStartToken, i));
            }

            token = result.ContinuationToken;
        }
        // Table Storage can return short pages with a continuation token; keep reading so the merge stays ordered.
        while (events.Count < PageSize && token != null && fetches < MergedMaxFetchesPerOrg);

        return new OrgBuffer(position.OrgId, events, token, skip);
    }

    /// <summary>
    /// K-way merges the buffers newest first, emitting only events that no unread event can precede.
    /// </summary>
    private static (List<IEvent> Data, Dictionary<Guid, int> Consumed) MergeSafePrefix(
        Dictionary<Guid, OrgBuffer> buffers)
    {
        var consumed = buffers.Keys.ToDictionary(id => id, _ => 0);
        var withMoreData = buffers.Values.Where(b => b.LastPageToken != null).ToList();
        var data = new List<IEvent>();

        // An org with more data but nothing buffered (fetch limit reached on empty pages) could hold the next
        // event, so nothing is safe. Positions still advance to its latest token, so the next request progresses.
        if (withMoreData.Any(b => b.Events.Count == 0))
        {
            return (data, consumed);
        }

        // Unread events of an org with more data are no newer than its oldest buffered event.
        var oldestBuffered = withMoreData.ToDictionary(b => b.OrgId, b => b.Events.MinBy(e => e.Event.Date)!.Event);

        while (data.Count < PageSize)
        {
            OrgBuffer? next = null;
            foreach (var buffer in buffers.Values)
            {
                if (consumed[buffer.OrgId] < buffer.Events.Count &&
                    (next == null || Precedes(Head(buffer), buffer.OrgId, Head(next), next.OrgId)))
                {
                    next = buffer;
                }
            }

            if (next == null)
            {
                break;
            }

            var candidate = Head(next);
            var candidateOrgId = next.OrgId;
            var isSafe = oldestBuffered.All(oldest =>
                oldest.Key == candidateOrgId || Precedes(candidate, candidateOrgId, oldest.Value, oldest.Key));
            if (!isSafe)
            {
                break;
            }

            data.Add(candidate);
            consumed[candidateOrgId]++;
        }

        return (data, consumed);

        IEvent Head(OrgBuffer buffer) => buffer.Events[consumed[buffer.OrgId]].Event;
    }

    /// <summary>
    /// The merged order: date descending, then organization ID ascending. Within an organization, repository
    /// order is preserved by the merge itself.
    /// </summary>
    private static bool Precedes(IEvent a, Guid aOrgId, IEvent b, Guid bOrgId) =>
        a.Date > b.Date || (a.Date == b.Date && aOrgId.CompareTo(bOrgId) < 0);

    private static ProviderClientEventsPosition NextPosition(OrgBuffer buffer, int consumed)
    {
        if (consumed < buffer.Events.Count)
        {
            var firstUnemitted = buffer.Events[consumed];
            return new ProviderClientEventsPosition
            {
                OrgId = buffer.OrgId,
                InnerToken = firstUnemitted.PageStartToken,
                Skip = firstUnemitted.IndexInPage,
            };
        }

        if (buffer.LastPageToken != null)
        {
            return new ProviderClientEventsPosition
            {
                OrgId = buffer.OrgId,
                InnerToken = buffer.LastPageToken,
                Skip = buffer.RemainingSkip,
            };
        }

        return new ProviderClientEventsPosition { OrgId = buffer.OrgId, Exhausted = true };
    }

    /// <summary>
    /// Validates the request's continuation token, if any, and resolves the date window. With a token, the window
    /// comes from the token so it stays pinned across pages; otherwise the end is clamped to now.
    /// </summary>
    private (ProviderClientEventsCursor? Cursor, DateRange Window) ReadCursor(ProviderClientEventsRequest request,
        ProviderClientEventsMode mode)
    {
        if (string.IsNullOrEmpty(request.ContinuationToken))
        {
            var now = _timeProvider.GetUtcNow().UtcDateTime;
            return (null, new DateRange(request.Start, request.End < now ? request.End : now));
        }

        var cursor = _cursorProtector.Unprotect(request.ContinuationToken, request.ProviderId, mode,
            ProviderClientEventsCursorProtector.ComputeFilterHash(request.OrganizationIds));
        return (cursor, new DateRange(cursor.Start, cursor.End));
    }

    private static ProviderClientEventsCursor NewCursor(ProviderClientEventsRequest request,
        ProviderClientEventsMode mode, DateRange window) => new()
        {
            Mode = mode,
            ProviderId = request.ProviderId,
            Start = window.Start,
            End = window.End,
            FilterHash = ProviderClientEventsCursorProtector.ComputeFilterHash(request.OrganizationIds),
        };

    private async Task<HashSet<Guid>> GetLinkedOrganizationIdsAsync(Guid providerId) =>
        (await _providerOrganizationRepository.GetManyDetailsByProviderAsync(providerId))
            .Select(po => po.OrganizationId)
            .ToHashSet();

    /// <summary>
    /// The provider's linked client organizations, narrowed to the filter, that have <c>UseEvents</c>, sorted with
    /// <see cref="Guid.CompareTo(Guid)"/>. Disabled organizations are included; their history stays readable.
    /// </summary>
    /// <exception cref="BadRequestException">The filter includes an organization that isn't linked.</exception>
    private async Task<List<Guid>> GetEligibleOrganizationIdsAsync(Guid providerId,
        IReadOnlyCollection<Guid>? organizationIds)
    {
        var ids = await GetLinkedOrganizationIdsAsync(providerId);

        if (organizationIds is { Count: > 0 })
        {
            // Generic message: don't reveal which IDs failed.
            if (!organizationIds.All(ids.Contains))
            {
                throw new BadRequestException("One or more organizations are invalid.");
            }

            ids.IntersectWith(organizationIds);
        }

        if (ids.Count == 0)
        {
            return [];
        }

        return (await _organizationRepository.GetManyByIdsAsync(ids))
            .Where(o => o.UseEvents && ids.Contains(o.Id))
            .Select(o => o.Id)
            .Order()
            .ToList();
    }

    private readonly record struct DateRange(DateTime Start, DateTime End);

    /// <summary>
    /// An event and where to find it again: its index within the page that starts at <see cref="PageStartToken"/>.
    /// </summary>
    private sealed record BufferedEvent(IEvent Event, string? PageStartToken, int IndexInPage);

    private sealed record OrgBuffer(Guid OrgId, List<BufferedEvent> Events, string? LastPageToken, int RemainingSkip);
}
