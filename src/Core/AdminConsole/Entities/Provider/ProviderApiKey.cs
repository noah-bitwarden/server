using System.ComponentModel.DataAnnotations;
using Bit.Core.AdminConsole.Enums.Provider;
using Bit.Core.Entities;
using Bit.Core.Utilities;

namespace Bit.Core.AdminConsole.Entities.Provider;

/// <summary>
/// An API key belonging to a <see cref="Provider"/>.
/// Analogous to <see cref="Bit.Core.Entities.OrganizationApiKey"/> for organizations.
/// </summary>
public class ProviderApiKey : ITableObject<Guid>
{
    /// <summary>
    /// A unique identifier for the provider API key.
    /// </summary>
    public Guid Id { get; set; }
    /// <summary>
    /// The ID of the <see cref="Provider"/> that owns the API key.
    /// </summary>
    public Guid ProviderId { get; set; }
    /// <summary>
    /// The type of API key, which determines what it grants access to. A provider may have at most one key per type.
    /// </summary>
    public ProviderApiKeyType Type { get; set; }
    /// <summary>
    /// The API key value.
    /// </summary>
    [MaxLength(30)]
    public string ApiKey { get; set; } = null!;
    /// <summary>
    /// The date the API key was created or last rotated.
    /// </summary>
    public DateTime RevisionDate { get; set; }

    public void SetNewId()
    {
        Id = CoreHelpers.GenerateComb();
    }
}
