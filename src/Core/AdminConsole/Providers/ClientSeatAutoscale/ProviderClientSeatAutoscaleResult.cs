namespace Bit.Core.AdminConsole.Providers.ClientSeatAutoscale;

/// <summary>
/// The result of asking <see cref="IProviderClientSeatAutoscaler"/> to cover a seat shortfall. The default value is
/// <see cref="ProviderClientSeatAutoscaleOutcome.NotApplicable"/>.
/// </summary>
public readonly record struct ProviderClientSeatAutoscaleResult(ProviderClientSeatAutoscaleOutcome Outcome)
{
    /// <summary>
    /// Shown whenever autoscale can't cover an eligible MSP client's seat shortfall.
    /// </summary>
    public const string SeatLimitReachedMessage = "Seat limit has been reached. Contact your provider to add more seats.";

    /// <summary>
    /// Whether the organization is an eligible provider client. When <c>false</c>, the caller keeps its existing
    /// behavior.
    /// </summary>
    public bool Applies => Outcome != ProviderClientSeatAutoscaleOutcome.NotApplicable;

    public bool Succeeded => Outcome == ProviderClientSeatAutoscaleOutcome.Success;
}
