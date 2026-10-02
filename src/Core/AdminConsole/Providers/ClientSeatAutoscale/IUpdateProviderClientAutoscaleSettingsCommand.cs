using Bit.Core.AdminConsole.Entities.Provider;
using Bit.Core.AdminConsole.Utilities.v2.Results;

namespace Bit.Core.AdminConsole.Providers.ClientSeatAutoscale;

/// <summary>
/// Updates a client's seat autoscale settings. Callers are responsible for authorizing the actor as an admin of the
/// provider; this command checks only that the provider and client are eligible.
/// </summary>
public interface IUpdateProviderClientAutoscaleSettingsCommand
{
    Task<CommandResult<ProviderOrganization>> UpdateAsync(UpdateProviderClientAutoscaleSettingsRequest request);
}
