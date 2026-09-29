using Bit.Core.Dirt.Events.ProviderClientEvents;
using Bit.Core.Dirt.Events.ProviderClientEvents.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Bit.Core.Dirt.Events;

public static class EventsServiceCollectionExtensions
{
    /// <summary>
    /// Registers event read queries. Requires ASP.NET Data Protection and a <see cref="TimeProvider"/>.
    /// </summary>
    public static IServiceCollection AddEventQueries(this IServiceCollection services)
    {
        services.TryAddScoped<IGetProviderClientEventsQuery, GetProviderClientEventsQuery>();
        return services;
    }
}
