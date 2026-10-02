using Bit.Core.Platform.Mail.Mailer;

namespace Bit.Core.AdminConsole.Models.Mail.Mailer.ProviderClientSeatAutoscale;

/// <summary>
/// Tells provider admins that seat autoscale could not add seats to one of their client organizations.
/// </summary>
public class ProviderClientSeatAutoscaleBlockedView : BaseMailView
{
    public required string ClientName { get; set; }
    public required string PlanName { get; set; }
    public required string Reason { get; set; }
    public required string NextSteps { get; set; }
    public required string ClientsUrl { get; set; }
}

public class ProviderClientSeatAutoscaleBlocked : BaseMail<ProviderClientSeatAutoscaleBlockedView>
{
    public override required string Subject { get; set; }
}
