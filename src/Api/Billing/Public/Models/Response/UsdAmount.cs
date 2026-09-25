namespace Bit.Api.Billing.Public.Models;

internal static class UsdAmount
{
    /// <summary>
    /// Rounds a USD amount to cents (midpoints away from zero, matching the CSV export's currency formatting) and
    /// always gives it exactly two decimal places, so it serializes as e.g. <c>15.00</c> rather than <c>15</c> or <c>15.0000</c>.
    /// </summary>
    public static decimal RoundToCents(decimal amount) =>
        // Math.Round lowers the scale to 2 but never raises it; adding 0.00m raises it to 2.
        Math.Round(amount, 2, MidpointRounding.AwayFromZero) + 0.00m;
}
