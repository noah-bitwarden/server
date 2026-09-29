using System.ComponentModel.DataAnnotations;
using Bit.Core.Models.Data;

namespace Bit.Api.Dirt.Public.Models;

/// <summary>
/// An event log from one of a provider's client organizations.
/// </summary>
public class ProviderEventResponseModel : EventResponseModel
{
    public ProviderEventResponseModel(IEvent ev)
        : base(ev)
    {
        OrganizationId = ev.OrganizationId ??
            throw new ArgumentException("Client organization events must have an organization.", nameof(ev));
        ProviderId = ev.ProviderId;
    }

    /// <summary>
    /// The unique identifier of the client organization the event belongs to.
    /// </summary>
    /// <example>7c7fb7a2-06e4-4e3b-9d8a-b3c9014f8d2a</example>
    [Required]
    public Guid OrganizationId { get; set; }

    /// <summary>
    /// The unique identifier of the provider, when a member of the provider performed the event.
    /// Null when the event was performed by the client organization's own members, API keys, or the system.
    /// </summary>
    /// <example>2f1a8c35-9b8e-4f3a-8a61-b3c9014f8d2b</example>
    public Guid? ProviderId { get; set; }
}
