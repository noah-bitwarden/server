using Bit.Core.Dirt.Events.ProviderClientEvents;
using Bit.Core.Exceptions;
using Bit.Test.Common.AutoFixture.Attributes;
using Microsoft.AspNetCore.DataProtection;
using Xunit;

namespace Bit.Core.Test.Dirt.Events.ProviderClientEvents;

public class ProviderClientEventsCursorProtectorTests
{
    private readonly ProviderClientEventsCursorProtector _sut = new(new EphemeralDataProtectionProvider());

    [Theory, BitAutoData]
    public void RoundTrip_PreservesEveryField(Guid providerId, Guid orgA, Guid orgB)
    {
        var filterHash = ProviderClientEventsCursorProtector.ComputeFilterHash([orgA, orgB]);
        var cursor = new ProviderClientEventsCursor
        {
            Mode = ProviderClientEventsMode.Merged,
            ProviderId = providerId,
            Start = new DateTime(2026, 8, 1, 1, 2, 3, DateTimeKind.Utc),
            End = new DateTime(2026, 9, 1, 4, 5, 6, 789, DateTimeKind.Utc),
            FilterHash = filterHash,
            LastOrgId = orgA,
            OrgId = orgB,
            InnerToken = "inner-token",
            Positions =
            [
                new ProviderClientEventsPosition { OrgId = orgA, InnerToken = "a-token", Skip = 7 },
                new ProviderClientEventsPosition { OrgId = orgB, Exhausted = true },
            ],
        };

        var result = _sut.Unprotect(_sut.Protect(cursor), providerId, ProviderClientEventsMode.Merged, filterHash);

        Assert.Equal(cursor with { Positions = null }, result with { Positions = null });
        Assert.Equal(DateTimeKind.Utc, result.End.Kind);
        Assert.Equal(cursor.Positions, result.Positions);
    }

    [Theory, BitAutoData]
    public void Unprotect_TamperedToken_ThrowsBadRequest(Guid providerId)
    {
        var token = _sut.Protect(Cursor(providerId));
        var tampered = token[..^4] + (token[^4] == 'A' ? 'B' : 'A') + token[^3..];

        Assert.Throws<BadRequestException>(() => Unprotect(tampered, providerId));
        Assert.Throws<BadRequestException>(() => Unprotect("not-a-token", providerId));
    }

    [Theory, BitAutoData]
    public void Unprotect_TokenFromAnotherProvider_ThrowsBadRequest(Guid providerId, Guid otherProviderId)
    {
        var token = _sut.Protect(Cursor(otherProviderId));

        Assert.Throws<BadRequestException>(() => Unprotect(token, providerId));
    }

    [Theory, BitAutoData]
    public void Unprotect_ProviderIdMismatchInsidePayload_ThrowsBadRequest(Guid providerId, Guid otherProviderId)
    {
        // Protected for providerId, but claiming another provider in the payload.
        var protector = new EphemeralDataProtectionProvider();
        var sut = new ProviderClientEventsCursorProtector(protector);
        var payload = sut.Protect(Cursor(otherProviderId));
        var reprotected = protector.CreateProtector(ProviderClientEventsCursorProtector.Purpose, providerId.ToString())
            .Protect(protector.CreateProtector(ProviderClientEventsCursorProtector.Purpose, otherProviderId.ToString())
                .Unprotect(payload));

        Assert.Throws<BadRequestException>(() =>
            sut.Unprotect(reprotected, providerId, ProviderClientEventsMode.OrgMajor, EmptyFilterHash));
    }

    [Theory]
    [BitAutoData(nameof(ProviderClientEventsMode.SingleOrg))]
    [BitAutoData(nameof(ProviderClientEventsMode.Merged))]
    public void Unprotect_TokenFromAnotherMode_ThrowsBadRequest(string otherMode, Guid providerId)
    {
        var token = _sut.Protect(Cursor(providerId) with { Mode = Enum.Parse<ProviderClientEventsMode>(otherMode) });

        Assert.Throws<BadRequestException>(() => Unprotect(token, providerId));
    }

    [Theory, BitAutoData]
    public void Unprotect_ChangedFilter_ThrowsBadRequest(Guid providerId, Guid organizationId)
    {
        var token = _sut.Protect(Cursor(providerId));

        Assert.Throws<BadRequestException>(() => _sut.Unprotect(token, providerId, ProviderClientEventsMode.OrgMajor,
            ProviderClientEventsCursorProtector.ComputeFilterHash([organizationId])));
    }

    [Theory, BitAutoData]
    public void Unprotect_UnsupportedVersion_ThrowsBadRequest(Guid providerId)
    {
        var token = _sut.Protect(Cursor(providerId) with { Version = 2 });

        Assert.Throws<BadRequestException>(() => Unprotect(token, providerId));
    }

    [Theory, BitAutoData]
    public void ComputeFilterHash_IgnoresOrderAndDuplicates(Guid orgA, Guid orgB)
    {
        Assert.Equal(
            ProviderClientEventsCursorProtector.ComputeFilterHash([orgA, orgB]),
            ProviderClientEventsCursorProtector.ComputeFilterHash([orgB, orgA, orgB]));
        Assert.NotEqual(
            ProviderClientEventsCursorProtector.ComputeFilterHash([orgA]),
            ProviderClientEventsCursorProtector.ComputeFilterHash([orgA, orgB]));
        Assert.Equal(EmptyFilterHash, ProviderClientEventsCursorProtector.ComputeFilterHash([]));
    }

    private static readonly string EmptyFilterHash = ProviderClientEventsCursorProtector.ComputeFilterHash(null);

    private ProviderClientEventsCursor Unprotect(string token, Guid providerId) =>
        _sut.Unprotect(token, providerId, ProviderClientEventsMode.OrgMajor, EmptyFilterHash);

    private static ProviderClientEventsCursor Cursor(Guid providerId) => new()
    {
        Mode = ProviderClientEventsMode.OrgMajor,
        ProviderId = providerId,
        Start = DateTime.UtcNow.AddDays(-30),
        End = DateTime.UtcNow,
        FilterHash = EmptyFilterHash,
        LastOrgId = Guid.NewGuid(),
        InnerToken = "inner",
    };
}
