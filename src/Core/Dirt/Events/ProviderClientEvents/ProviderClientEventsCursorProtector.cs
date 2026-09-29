using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Bit.Core.Exceptions;
using Microsoft.AspNetCore.DataProtection;

namespace Bit.Core.Dirt.Events.ProviderClientEvents;

/// <summary>
/// Serializes <see cref="ProviderClientEventsCursor"/>s into opaque continuation tokens with ASP.NET Data Protection.
/// Tokens are protected per provider, so a token issued to one provider can't be unprotected for another.
/// </summary>
internal sealed class ProviderClientEventsCursorProtector
{
    public const int CurrentVersion = 1;
    public const string Purpose = "ProviderClientEvents";
    private const string InvalidTokenMessage = "Invalid continuation token.";

    private static readonly JsonSerializerOptions _serializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    private readonly IDataProtectionProvider _dataProtectionProvider;

    public ProviderClientEventsCursorProtector(IDataProtectionProvider dataProtectionProvider)
    {
        _dataProtectionProvider = dataProtectionProvider;
    }

    public string Protect(ProviderClientEventsCursor cursor) =>
        CreateProtector(cursor.ProviderId).Protect(JsonSerializer.Serialize(cursor, _serializerOptions));

    /// <exception cref="BadRequestException">
    /// The token is tampered, unsupported, or was issued for another provider, mode, or organization filter.
    /// </exception>
    public ProviderClientEventsCursor Unprotect(string token, Guid providerId, ProviderClientEventsMode mode,
        string filterHash)
    {
        ProviderClientEventsCursor? cursor;
        try
        {
            cursor = JsonSerializer.Deserialize<ProviderClientEventsCursor>(
                CreateProtector(providerId).Unprotect(token), _serializerOptions);
        }
        catch (Exception e) when (e is CryptographicException or FormatException or JsonException)
        {
            throw new BadRequestException(InvalidTokenMessage);
        }

        if (cursor is not { Version: CurrentVersion } ||
            cursor.ProviderId != providerId ||
            cursor.Mode != mode ||
            cursor.FilterHash != filterHash)
        {
            throw new BadRequestException(InvalidTokenMessage);
        }

        return cursor;
    }

    /// <summary>
    /// A stable hash of the requested organization filter, independent of order and duplicates.
    /// </summary>
    public static string ComputeFilterHash(IEnumerable<Guid>? organizationIds)
    {
        var sorted = (organizationIds ?? []).Distinct().Order();
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(string.Join(',', sorted)));
        return Convert.ToHexString(bytes, 0, 16);
    }

    private IDataProtector CreateProtector(Guid providerId) =>
        _dataProtectionProvider.CreateProtector(Purpose, providerId.ToString());
}
