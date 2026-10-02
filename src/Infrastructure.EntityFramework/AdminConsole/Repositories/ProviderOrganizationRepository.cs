// FIXME: Update this file to be null safe and then delete the line below
#nullable disable

using System.Data;
using AutoMapper;
using Bit.Core.AdminConsole.Entities.Provider;
using Bit.Core.AdminConsole.Models.Data.Provider;
using Bit.Core.AdminConsole.Repositories;
using Bit.Core.Enums;
using Bit.Infrastructure.EntityFramework.AdminConsole.Repositories.Queries;
using Bit.Infrastructure.EntityFramework.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Bit.Infrastructure.EntityFramework.AdminConsole.Repositories;

public class ProviderOrganizationRepository :
    Repository<ProviderOrganization, Models.Provider.ProviderOrganization, Guid>, IProviderOrganizationRepository
{
    public ProviderOrganizationRepository(IServiceScopeFactory serviceScopeFactory, IMapper mapper)
        : base(serviceScopeFactory, mapper, context => context.ProviderOrganizations)
    { }

    public async Task<ICollection<ProviderOrganization>> CreateManyAsync(IEnumerable<ProviderOrganization> providerOrganizations)
    {
        var entities = providerOrganizations.ToList();

        if (!entities.Any())
        {
            return default;
        }

        foreach (var providerOrganization in entities)
        {
            providerOrganization.SetNewId();
        }

        using (var scope = ServiceScopeFactory.CreateScope())
        {
            var dbContext = GetDatabaseContext(scope);
            await dbContext.AddRangeAsync(entities);
            await dbContext.SaveChangesAsync();
        }

        return entities;
    }

    public async Task<ICollection<ProviderOrganizationOrganizationDetails>> GetManyDetailsByProviderAsync(Guid providerId)
    {
        using (var scope = ServiceScopeFactory.CreateScope())
        {
            var dbContext = GetDatabaseContext(scope);
            var query = new ProviderOrganizationOrganizationDetailsReadByProviderIdQuery(providerId);
            var data = await query.Run(dbContext).ToListAsync();
            return data;
        }
    }

    public async Task<ProviderOrganization> GetByOrganizationId(Guid organizationId)
    {
        using var scope = ServiceScopeFactory.CreateScope();
        var dbContext = GetDatabaseContext(scope);
        return await GetDbSet(dbContext).Where(po => po.OrganizationId == organizationId).FirstOrDefaultAsync();
    }

    public async Task<IEnumerable<ProviderOrganizationProviderDetails>> GetManyByUserAsync(Guid userId)
    {
        using (var scope = ServiceScopeFactory.CreateScope())
        {
            var dbContext = GetDatabaseContext(scope);
            var query = new ProviderOrganizationReadByUserIdQuery(userId);
            var data = await query.Run(dbContext).ToListAsync();
            return data;
        }
    }

    public async Task<int> GetCountByOrganizationIdsAsync(IEnumerable<Guid> organizationIds)
    {
        var query = new ProviderOrganizationCountByOrganizationIdsQuery(organizationIds);
        return await GetCountFromQuery(query);
    }

    public async Task<ProviderOrganizationAutoscaleSeatsResult> TryAutoscaleSeatsAsync(Guid organizationId,
        string planName, int seatsToAdd, DateTime revisionDate, bool validateOnly = false)
    {
        using var scope = ServiceScopeFactory.CreateScope();
        var dbContext = GetDatabaseContext(scope);

        await using var transaction = await dbContext.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted);

        var result = await EvaluateAndApplyAutoscaleAsync(dbContext, organizationId, planName, seatsToAdd,
            revisionDate, validateOnly);

        await transaction.CommitAsync();

        return result;
    }

    /// <summary>
    /// Mirrors the <c>ProviderOrganization_TryAutoscaleSeats</c> stored procedure. Must run inside a transaction.
    /// </summary>
    private static async Task<ProviderOrganizationAutoscaleSeatsResult> EvaluateAndApplyAutoscaleAsync(
        DatabaseContext dbContext, Guid organizationId, string planName, int seatsToAdd, DateTime revisionDate,
        bool validateOnly)
    {
        var client = await (
                from po in dbContext.ProviderOrganizations
                join o in dbContext.Organizations on po.OrganizationId equals o.Id
                where po.OrganizationId == organizationId &&
                      o.Status == OrganizationStatusType.Managed &&
                      o.Seats != null
                select new { po.ProviderId, o.PlanType, po.AutoscaleEnabled, po.AutoscaleSeatLimit })
            .FirstOrDefaultAsync();

        if (client == null)
        {
            return ProviderOrganizationAutoscaleSeatsResult.ClientNotManaged;
        }

        if (!client.AutoscaleEnabled)
        {
            return ProviderOrganizationAutoscaleSeatsResult.NotEnabled;
        }

        var providerPlans = dbContext.ProviderPlans
            .Where(pp => pp.ProviderId == client.ProviderId && pp.PlanType == client.PlanType);

        // A self-assignment takes the plan row's write lock, standing in for the stored procedure's UPDLOCK/HOLDLOCK
        await providerPlans.ExecuteUpdateAsync(s =>
            s.SetProperty(pp => pp.AllocatedSeats, pp => pp.AllocatedSeats));

        var providerPlan = await providerPlans
            .Select(pp => new { pp.Id, pp.SeatMinimum, pp.PurchasedSeats, pp.AllocatedSeats })
            .FirstOrDefaultAsync();

        if (providerPlan is not { SeatMinimum: not null, PurchasedSeats: not null, AllocatedSeats: not null })
        {
            return ProviderOrganizationAutoscaleSeatsResult.NoPool;
        }

        var clientSeats = await dbContext.Organizations
            .Where(o => o.Id == organizationId)
            .Select(o => o.Seats ?? 0)
            .FirstAsync();

        // Mirrors ProviderBillingService.GetAssignedSeatTotalAsync: managed clients on the same plan name
        var assignedSeats = await (
                from po in dbContext.ProviderOrganizations
                join o in dbContext.Organizations on po.OrganizationId equals o.Id
                where po.ProviderId == client.ProviderId &&
                      o.Status == OrganizationStatusType.Managed &&
                      o.Plan == planName
                select o.Seats ?? 0)
            .SumAsync();

        if (assignedSeats + seatsToAdd > providerPlan.SeatMinimum.Value)
        {
            return ProviderOrganizationAutoscaleSeatsResult.PoolExhausted;
        }

        if (client.AutoscaleSeatLimit.HasValue && clientSeats + seatsToAdd > client.AutoscaleSeatLimit.Value)
        {
            return ProviderOrganizationAutoscaleSeatsResult.ClientLimitReached;
        }

        if (validateOnly)
        {
            return ProviderOrganizationAutoscaleSeatsResult.Success;
        }

        await dbContext.Organizations
            .Where(o => o.Id == organizationId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(o => o.Seats, o => o.Seats + seatsToAdd)
                .SetProperty(o => o.RevisionDate, revisionDate));

        // Matches ProviderBillingService.ScaleSeats when the total stays at or below the seat minimum
        var allocatedSeats = assignedSeats + seatsToAdd;
        await dbContext.ProviderPlans
            .Where(pp => pp.Id == providerPlan.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(pp => pp.AllocatedSeats, allocatedSeats));

        return ProviderOrganizationAutoscaleSeatsResult.Success;
    }
}
