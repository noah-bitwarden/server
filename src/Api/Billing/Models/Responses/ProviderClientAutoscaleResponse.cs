using Bit.Core.AdminConsole.Entities.Provider;

namespace Bit.Api.Billing.Models.Responses;

public record ProviderClientAutoscaleResponse(bool AutoscaleEnabled, int? AutoscaleSeatLimit)
{
    public static ProviderClientAutoscaleResponse From(ProviderOrganization providerOrganization) =>
        new(providerOrganization.AutoscaleEnabled, providerOrganization.AutoscaleSeatLimit);
}
