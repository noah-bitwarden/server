namespace Bit.Api.Billing.Models.Requests;

public class UpdateClientAutoscaleRequestBody
{
    /// <summary>
    /// Whether the client may add seats from the provider's seat minimum when it runs out.
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// The most seats autoscale may grow the client to. Omit for no per-client limit.
    /// </summary>
    public int? SeatLimit { get; set; }
}
