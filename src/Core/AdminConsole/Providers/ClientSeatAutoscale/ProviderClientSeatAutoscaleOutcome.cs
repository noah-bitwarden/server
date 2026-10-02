namespace Bit.Core.AdminConsole.Providers.ClientSeatAutoscale;

public enum ProviderClientSeatAutoscaleOutcome
{
    /// <summary>
    /// The organization is not an eligible provider client, so the caller keeps its existing seat behavior.
    /// </summary>
    NotApplicable = 0,
    Success,
    NotEnabled,
    NoPool,
    PoolExhausted,
    ClientLimitReached,
    ClientNotManaged,
}
