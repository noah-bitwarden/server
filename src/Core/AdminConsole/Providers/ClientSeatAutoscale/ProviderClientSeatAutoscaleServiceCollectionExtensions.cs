using Bit.Core.Settings;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Bit.Core.AdminConsole.Providers.ClientSeatAutoscale;

public static class ProviderClientSeatAutoscaleServiceCollectionExtensions
{
    /// <summary>
    /// Registers provider client seat autoscale. Lives in Core because guards in Scim and Sso, which don't load the
    /// commercial assemblies, depend on it.
    /// </summary>
    public static IServiceCollection AddProviderClientSeatAutoscale(this IServiceCollection services,
        GlobalSettings globalSettings)
    {
        services.AddExtendedCache(ProviderClientSeatAutoscaler.NotificationCacheName, globalSettings);
        services.TryAddScoped<IProviderClientSeatAutoscaler, ProviderClientSeatAutoscaler>();
        services.TryAddScoped<IUpdateProviderClientAutoscaleSettingsCommand, UpdateProviderClientAutoscaleSettingsCommand>();
        return services;
    }
}
