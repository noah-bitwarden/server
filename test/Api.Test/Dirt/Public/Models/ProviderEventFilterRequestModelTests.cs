using Bit.Api.Dirt.Public.Models;
using Bit.Core.Exceptions;
using Xunit;

namespace Bit.Api.Test.Dirt.Public.Models;

public class ProviderEventFilterRequestModelTests
{
    [Fact]
    public void ToQueryRequest_NoDates_DefaultsToThirtyDaysEndingNow()
    {
        var providerId = Guid.NewGuid();
        var before = DateTime.UtcNow;

        var request = new ProviderEventFilterRequestModel().ToQueryRequest(providerId);

        Assert.Equal(providerId, request.ProviderId);
        Assert.InRange(request.End, before, DateTime.UtcNow);
        Assert.Equal(request.End.Date.AddDays(-30), request.Start);
        Assert.Null(request.OrganizationIds);
        Assert.Null(request.ContinuationToken);
    }

    [Fact]
    public void ToQueryRequest_OnlyEnd_StartsThirtyDaysBeforeEnd()
    {
        var end = new DateTime(2026, 9, 1, 15, 30, 0, DateTimeKind.Utc);

        var request = new ProviderEventFilterRequestModel { End = end }.ToQueryRequest(Guid.NewGuid());

        Assert.Equal(end.AddDays(-30), request.Start);
        Assert.Equal(end, request.End);
    }

    [Fact]
    public void ToQueryRequest_OnlyStart_EndsNow()
    {
        var start = DateTime.UtcNow.AddDays(-3);
        var before = DateTime.UtcNow;

        var request = new ProviderEventFilterRequestModel { Start = start }.ToQueryRequest(Guid.NewGuid());

        Assert.Equal(start, request.Start);
        Assert.InRange(request.End, before, DateTime.UtcNow);
    }

    [Fact]
    public void ToQueryRequest_ReversedDates_AreSwapped()
    {
        var earlier = new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc);
        var later = new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc);

        var request = new ProviderEventFilterRequestModel { Start = later, End = earlier }
            .ToQueryRequest(Guid.NewGuid());

        Assert.Equal(earlier, request.Start);
        Assert.Equal(later, request.End);
    }

    [Fact]
    public void ToQueryRequest_RangeOver367Days_ThrowsBadRequest()
    {
        var model = new ProviderEventFilterRequestModel
        {
            Start = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            End = new DateTime(2026, 1, 5, 0, 0, 0, DateTimeKind.Utc),
        };

        Assert.Throws<BadRequestException>(() => model.ToQueryRequest(Guid.NewGuid()));
    }

    [Fact]
    public void ToQueryRequest_WithContinuationToken_IgnoresDates()
    {
        // The query takes the date range from the token, so invalid dates aren't rejected here.
        var model = new ProviderEventFilterRequestModel
        {
            Start = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            End = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            ContinuationToken = "token",
        };

        var request = model.ToQueryRequest(Guid.NewGuid());

        Assert.Equal("token", request.ContinuationToken);
    }
}
