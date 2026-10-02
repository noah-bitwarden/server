using Bit.Core.AdminConsole.OrganizationFeatures.OrganizationUsers.InviteUsers.Validation.PasswordManager;
using Bit.Core.AdminConsole.Providers.ClientSeatAutoscale;
using Bit.Core.AdminConsole.Utilities.Errors;

namespace Bit.Core.AdminConsole.OrganizationFeatures.OrganizationUsers.InviteUsers.Validation.Provider;

public record ProviderBillableSeatLimitError(InviteOrganizationProvider InvalidRequest) : Error<InviteOrganizationProvider>(GetErrorMessage(InvalidRequest), InvalidRequest)
{
    private static string GetErrorMessage(InviteOrganizationProvider invalidRequest) =>
        string.Format(Code, invalidRequest.Seats);

    public const string Code = "Seat limit of {0} has been reached. Contact your provider to purchase additional seats.";
}

public record ProviderResellerSeatLimitError(InviteOrganizationProvider InvalidRequest) : Error<InviteOrganizationProvider>(GetErrorMessage(InvalidRequest), InvalidRequest)
{
    private static string GetErrorMessage(InviteOrganizationProvider invalidRequest) =>
        string.Format(Code, invalidRequest.Seats);

    public const string Code = "Seat limit of {0} has been reached. Contact your provider to purchase additional seats.";
}

/// <summary>
/// An eligible MSP client is out of seats and provider client seat autoscale can't cover the shortfall.
/// </summary>
public record ProviderClientSeatLimitReachedError(PasswordManagerSubscriptionUpdate InvalidRequest)
    : Error<PasswordManagerSubscriptionUpdate>(Code, InvalidRequest)
{
    public const string Code = ProviderClientSeatAutoscaleResult.SeatLimitReachedMessage;
}
