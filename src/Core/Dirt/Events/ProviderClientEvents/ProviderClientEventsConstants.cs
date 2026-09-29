namespace Bit.Core.Dirt.Events.ProviderClientEvents;

public static class ProviderClientEventsConstants
{
    /// <summary>
    /// Events per page. Matches the existing event APIs.
    /// </summary>
    public const int PageSize = 50;

    /// <summary>
    /// The most client organizations that can be read in merged (chronological) mode.
    /// </summary>
    public const int MergedMaxOrgs = 10;

    /// <summary>
    /// The most <c>GetManyByOrganizationAsync</c> calls made by one org-major request.
    /// </summary>
    public const int OrgMajorQueryBudget = 20;

    /// <summary>
    /// The most page fetches made per organization by one merged request.
    /// </summary>
    public const int MergedMaxFetchesPerOrg = 5;

    /// <summary>
    /// The most organizations fetched in parallel by one merged request.
    /// </summary>
    public const int MergedConcurrency = 5;
}
