using Bit.Core.AdminConsole.Utilities.v2;

namespace Bit.Core.AdminConsole.Providers.ClientSeatAutoscale;

public record ProviderNotFound() : NotFoundError("Provider not found.");

public record ProviderNotEligibleForAutoscale()
    : BadRequestError("Seat autoscale is only available for billable MSP providers.");

public record ClientNotFound() : NotFoundError("Client organization not found.");

public record ClientNotManagedByProvider()
    : BadRequestError("Seat autoscale is only available for clients billed through the provider.");

public record InvalidAutoscaleSeatLimit()
    : BadRequestError("The seat limit must be greater than zero and at least the client's current seat count.");
