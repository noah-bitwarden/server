using System.Net;
using System.Text.Json;
using Bit.Api.Billing.Public.Controllers;
using Bit.Api.Billing.Public.Models;
using Bit.Api.Models.Public.Response;
using Bit.Core.AdminConsole.Entities.Provider;
using Bit.Core.AdminConsole.Enums.Provider;
using Bit.Core.AdminConsole.Repositories;
using Bit.Core.Billing.Providers.Entities;
using Bit.Core.Billing.Providers.Repositories;
using Bit.Core.Context;
using Bit.Core.Exceptions;
using Bit.Test.Common.AutoFixture;
using Bit.Test.Common.AutoFixture.Attributes;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using Xunit;

namespace Bit.Api.Test.Billing.Public.Controllers;

[ControllerCustomize(typeof(ProviderController))]
[SutProviderCustomize]
public class ProviderControllerTests
{
    private const string SeptemberInvoiceId = "in_september";
    private const string OctoberInvoiceId = "in_october";

    private static readonly Guid _clientA = Guid.NewGuid();
    private static readonly Guid _clientB = Guid.NewGuid();

    private static readonly DateTime _september = new(2026, 9, 1, 3, 15, 0, DateTimeKind.Unspecified);
    private static readonly DateTime _october = new(2026, 10, 1, 3, 15, 0, DateTimeKind.Unspecified);

    // GetInvoices

    [Theory, BitAutoData]
    public async Task GetInvoices_GroupsByInvoice_WithClientAndUnassignedRows(SutProvider<ProviderController> sutProvider)
    {
        var provider = SetupProvider(sutProvider);
        SetupItems(sutProvider, provider.Id, BuildItems(provider.Id));

        var result = await GetInvoicesAsync(sutProvider, "2026-09-01", "2026-10-31");

        Assert.Equal(2, result.Data.Count());
        var october = result.Data.First();
        var september = result.Data.Last();
        Assert.Equal(_october, october.InvoiceDate);
        Assert.Equal(DateTimeKind.Utc, october.InvoiceDate.Kind);
        Assert.Equal(_september, september.InvoiceDate);

        Assert.Collection(september.LineItems,
            item =>
            {
                Assert.Equal("client", item.Type);
                Assert.Equal(_clientA, item.OrganizationId);
                Assert.Equal("Acme Corp", item.ClientName);
                Assert.Equal("Enterprise (Monthly)", item.PlanName);
                Assert.Equal(25, item.AssignedSeats);
                Assert.Equal(22, item.UsedSeats);
                Assert.Equal(3, item.RemainingSeats);
                Assert.Equal(75.00m, item.EstimatedTotal);
            },
            item =>
            {
                Assert.Equal("client", item.Type);
                Assert.Equal(_clientB, item.OrganizationId);
                Assert.Equal("Beta LLC", item.ClientName);
            },
            item =>
            {
                Assert.Equal("unassigned", item.Type);
                Assert.Null(item.OrganizationId);
                Assert.Null(item.ClientName);
                Assert.Equal(100, item.AssignedSeats);
                Assert.Equal(0, item.UsedSeats);
                Assert.Equal(100, item.RemainingSeats);
                Assert.Equal(300.00m, item.EstimatedTotal);
            });
        Assert.Null(result.ContinuationToken);
    }

    [Theory, BitAutoData]
    public async Task GetInvoices_NoDates_ReturnsOnlyMostRecentInvoice(SutProvider<ProviderController> sutProvider)
    {
        var provider = SetupProvider(sutProvider);
        SetupItems(sutProvider, provider.Id, BuildItems(provider.Id));

        var result = await GetInvoicesAsync(sutProvider, null, null);

        var invoice = Assert.Single(result.Data);
        Assert.Equal(_october, invoice.InvoiceDate);
    }

    [Theory]
    [BitAutoData("2026-09-01", "2026-09-01", 1)] // start == end is that single day
    [BitAutoData("2026-09-02", "2026-10-01", 1)] // end is inclusive
    [BitAutoData("2026-10-01", "", 1)] // start only
    [BitAutoData("", "2026-09-30", 1)] // end only
    [BitAutoData("2026-09-02", "2026-09-30", 0)]
    public async Task GetInvoices_FiltersByInclusiveDateRange(
        string start, string end, int expectedCount, SutProvider<ProviderController> sutProvider)
    {
        var provider = SetupProvider(sutProvider);
        SetupItems(sutProvider, provider.Id, BuildItems(provider.Id));

        var result = await GetInvoicesAsync(sutProvider, start, end);

        Assert.Equal(expectedCount, result.Data.Count());
    }

    [Theory, BitAutoData]
    public async Task GetInvoices_NoItems_ReturnsEmptyList(SutProvider<ProviderController> sutProvider)
    {
        var provider = SetupProvider(sutProvider);
        SetupItems(sutProvider, provider.Id, []);

        var result = await GetInvoicesAsync(sutProvider, null, null);

        Assert.Empty(result.Data);
    }

    // GetClientInvoices

    [Theory, BitAutoData]
    public async Task GetClientInvoices_ReturnsOnlyThatClientsRows(SutProvider<ProviderController> sutProvider)
    {
        var provider = SetupProvider(sutProvider);
        SetupItems(sutProvider, provider.Id, BuildItems(provider.Id));

        var result = await GetClientInvoicesAsync(sutProvider, _clientA, "2026-01-01", "2026-12-31");

        Assert.Equal(2, result.Data.Count());
        Assert.All(result.Data, item => Assert.Equal(_clientA, item.OrganizationId));
        Assert.Equal(_october, result.Data.First().InvoiceDate);
        Assert.Equal(DateTimeKind.Utc, result.Data.First().InvoiceDate.Kind);
        var september = result.Data.Last();
        Assert.Equal("Acme Corp", september.ClientName);
        Assert.Equal("Enterprise (Monthly)", september.PlanName);
        Assert.Equal(25, september.AssignedSeats);
        Assert.Equal(22, september.UsedSeats);
        Assert.Equal(3, september.RemainingSeats);
        Assert.Equal(75.00m, september.EstimatedTotal);
    }

    [Theory, BitAutoData]
    public async Task GetClientInvoices_NoDates_ReturnsClientRowFromLatestInvoiceContainingClient(
        SutProvider<ProviderController> sutProvider)
    {
        var provider = SetupProvider(sutProvider);
        // The latest invoice (October) does not include client B.
        SetupItems(sutProvider, provider.Id, BuildItems(provider.Id));

        var clientA = await GetClientInvoicesAsync(sutProvider, _clientA, null, null);
        var clientB = await GetClientInvoicesAsync(sutProvider, _clientB, null, null);

        Assert.Equal(_october, Assert.Single(clientA.Data).InvoiceDate);
        var clientBItem = Assert.Single(clientB.Data);
        Assert.Equal(_september, clientBItem.InvoiceDate);
        Assert.Equal("Beta LLC", clientBItem.ClientName);
    }

    [Theory]
    [BitAutoData("2026-09-01", "2026-09-01", 1)]
    [BitAutoData("2026-09-02", "2026-10-01", 1)]
    [BitAutoData("2026-10-01", "", 1)]
    [BitAutoData("", "2026-09-30", 1)]
    public async Task GetClientInvoices_FiltersByInclusiveDateRange(
        string start, string end, int expectedCount, SutProvider<ProviderController> sutProvider)
    {
        var provider = SetupProvider(sutProvider);
        SetupItems(sutProvider, provider.Id, BuildItems(provider.Id));

        var result = await GetClientInvoicesAsync(sutProvider, _clientA, start, end);

        Assert.Equal(expectedCount, result.Data.Count());
    }

    [Theory, BitAutoData]
    public async Task GetClientInvoices_ItemsOnlyOutsideRange_ReturnsEmptyList(SutProvider<ProviderController> sutProvider)
    {
        var provider = SetupProvider(sutProvider);
        SetupItems(sutProvider, provider.Id, BuildItems(provider.Id));

        var result = await GetClientInvoicesAsync(sutProvider, _clientA, "2025-01-01", "2025-01-31");

        Assert.Empty(result.Data);
    }

    [Theory, BitAutoData]
    public async Task GetClientInvoices_UnknownOrganization_ReturnsNotFound(
        Guid unknownOrganizationId, SutProvider<ProviderController> sutProvider)
    {
        var provider = SetupProvider(sutProvider);
        SetupItems(sutProvider, provider.Id, BuildItems(provider.Id));

        var result = await sutProvider.Sut.GetClientInvoices(unknownOrganizationId, new ProviderInvoiceFilterRequestModel());

        Assert.IsType<NotFoundResult>(result);
    }

    [Theory, BitAutoData]
    public async Task GetClientInvoices_AnotherProvidersOrganization_ReturnsNotFound(
        Guid otherProviderId, SutProvider<ProviderController> sutProvider)
    {
        var provider = SetupProvider(sutProvider);
        SetupItems(sutProvider, provider.Id, BuildItems(provider.Id));
        var otherClient = Guid.NewGuid();
        SetupItems(sutProvider, otherProviderId, [Item(otherProviderId, "in_other", otherClient, _september)]);

        var result = await sutProvider.Sut.GetClientInvoices(otherClient, new ProviderInvoiceFilterRequestModel());

        Assert.IsType<NotFoundResult>(result);
        await sutProvider.GetDependency<IProviderInvoiceItemRepository>().DidNotReceive().GetByProviderId(otherProviderId);
    }

    // One invoice, one invoiceDate (both endpoints)

    [Theory, BitAutoData]
    public async Task InvoiceDate_IsGroupMinimum_AndIdenticalOnBothEndpoints(SutProvider<ProviderController> sutProvider)
    {
        var provider = SetupProvider(sutProvider);
        var earliest = new DateTime(2026, 9, 30, 23, 59, 59, 900, DateTimeKind.Unspecified);
        // Rows of one invoice recorded across UTC midnight; client A's own row is not the earliest.
        SetupItems(sutProvider, provider.Id,
        [
            Item(provider.Id, SeptemberInvoiceId, _clientB, earliest, "Beta LLC"),
            Item(provider.Id, SeptemberInvoiceId, _clientA, earliest.AddMilliseconds(200), "Acme Corp"),
            Item(provider.Id, SeptemberInvoiceId, null, earliest.AddSeconds(3), "Unassigned seats"),
        ]);

        var invoice = Assert.Single((await GetInvoicesAsync(sutProvider, null, null)).Data);
        var clientA = Assert.Single((await GetClientInvoicesAsync(sutProvider, _clientA, null, null)).Data);
        var clientB = Assert.Single((await GetClientInvoicesAsync(sutProvider, _clientB, null, null)).Data);

        Assert.Equal(earliest, invoice.InvoiceDate);
        Assert.Equal(invoice.InvoiceDate, clientA.InvoiceDate);
        Assert.Equal(invoice.InvoiceDate, clientB.InvoiceDate);
        Assert.Equal(DateTimeKind.Utc, clientA.InvoiceDate.Kind);
    }

    [Theory]
    [BitAutoData("2026-09-30", "2026-09-30", true)] // the invoice date's day: whole invoice in
    [BitAutoData("2026-10-01", "2026-10-01", false)] // a later row's day: whole invoice out
    [BitAutoData("2026-10-01", "", false)]
    [BitAutoData("", "2026-09-30", true)]
    public async Task RangeBoundaryBetweenRowsOfOneInvoice_IncludesOrExcludesWholeInvoice_OnBothEndpoints(
        string start, string end, bool included, SutProvider<ProviderController> sutProvider)
    {
        var provider = SetupProvider(sutProvider);
        var beforeMidnight = new DateTime(2026, 9, 30, 23, 59, 59, 900, DateTimeKind.Unspecified);
        SetupItems(sutProvider, provider.Id,
        [
            Item(provider.Id, SeptemberInvoiceId, _clientB, beforeMidnight, "Beta LLC"),
            Item(provider.Id, SeptemberInvoiceId, _clientA, beforeMidnight.AddMilliseconds(200), "Acme Corp"),
        ]);

        var invoices = await GetInvoicesAsync(sutProvider, start, end);
        var clientA = await GetClientInvoicesAsync(sutProvider, _clientA, start, end);
        var clientB = await GetClientInvoicesAsync(sutProvider, _clientB, start, end);

        var expectedCount = included ? 1 : 0;
        Assert.Equal(expectedCount, invoices.Data.Count());
        Assert.Equal(expectedCount, clientA.Data.Count());
        Assert.Equal(expectedCount, clientB.Data.Count());
        if (included)
        {
            Assert.Equal(2, invoices.Data.Single().LineItems.Count());
        }
    }

    [Theory]
    [BitAutoData(23, 59, 59, true)] // last second of the end day: included
    [BitAutoData(0, 0, 0, false)] // 00:00:00Z the next day: excluded
    public async Task EndDate_IsInclusiveThroughEndOfDay(
        int hour, int minute, int second, bool included, SutProvider<ProviderController> sutProvider)
    {
        var provider = SetupProvider(sutProvider);
        var created = hour == 0
            ? new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Unspecified)
            : new DateTime(2026, 9, 30, hour, minute, second, DateTimeKind.Unspecified).AddTicks(9_999_999);
        SetupItems(sutProvider, provider.Id, [Item(provider.Id, SeptemberInvoiceId, _clientA, created)]);

        var invoices = await GetInvoicesAsync(sutProvider, "2026-09-01", "2026-09-30");
        var clientA = await GetClientInvoicesAsync(sutProvider, _clientA, "2026-09-01", "2026-09-30");

        Assert.Equal(included ? 1 : 0, invoices.Data.Count());
        Assert.Equal(included ? 1 : 0, clientA.Data.Count());
    }

    // Date parsing

    [Theory]
    [BitAutoData("2026-09-01", "2026-09-01")]
    [BitAutoData("2028-02-29", "2028-02-29")] // leap day
    [BitAutoData("2026-09-01", "2026-09-30")]
    [BitAutoData("0001-01-01", "9999-12-31")] // end has no next day
    public async Task ValidIsoDates_AreAccepted(string start, string end, SutProvider<ProviderController> sutProvider)
    {
        var provider = SetupProvider(sutProvider);
        SetupItems(sutProvider, provider.Id, BuildItems(provider.Id));

        await GetInvoicesAsync(sutProvider, start, end);
        await GetClientInvoicesAsync(sutProvider, _clientA, start, end);
    }

    [Theory]
    [BitAutoData("20260901")]
    [BitAutoData("2026-9-1")]
    [BitAutoData("2026-13-01")]
    [BitAutoData("2026-02-30")]
    [BitAutoData("09/01/2026")]
    [BitAutoData("2026-09-01T00:00:00Z")]
    [BitAutoData(" 2026-09-01")]
    [BitAutoData("2026-09-01 ")]
    public async Task InvalidDates_ThrowBadRequest_PerParameter(string value, SutProvider<ProviderController> sutProvider)
    {
        var provider = SetupProvider(sutProvider);
        SetupItems(sutProvider, provider.Id, BuildItems(provider.Id));

        foreach (var (request, name) in new[]
                 {
                     (new ProviderInvoiceFilterRequestModel { Start = value }, "start"),
                     (new ProviderInvoiceFilterRequestModel { End = value }, "end"),
                 })
        {
            var expected = $"The {name} date must be a valid date in yyyy-MM-dd format.";
            Assert.Equal(expected,
                (await Assert.ThrowsAsync<BadRequestException>(() => sutProvider.Sut.GetInvoices(request))).Message);
            Assert.Equal(expected,
                (await Assert.ThrowsAsync<BadRequestException>(() =>
                    sutProvider.Sut.GetClientInvoices(_clientA, request))).Message);
        }
    }

    [Theory, BitAutoData]
    public async Task StartAfterEnd_ThrowsBadRequest(SutProvider<ProviderController> sutProvider)
    {
        var provider = SetupProvider(sutProvider);
        SetupItems(sutProvider, provider.Id, BuildItems(provider.Id));
        var request = new ProviderInvoiceFilterRequestModel { Start = "2026-10-01", End = "2026-09-30" };

        const string expected = "The start date must not be after the end date.";
        Assert.Equal(expected,
            (await Assert.ThrowsAsync<BadRequestException>(() => sutProvider.Sut.GetInvoices(request))).Message);
        Assert.Equal(expected,
            (await Assert.ThrowsAsync<BadRequestException>(() =>
                sutProvider.Sut.GetClientInvoices(_clientA, request))).Message);
    }

    // Authorization

    [Theory]
    [BitAutoData(ProviderType.BusinessUnit, true, ProviderStatusType.Billable)]
    [BitAutoData(ProviderType.Reseller, true, ProviderStatusType.Billable)]
    [BitAutoData(ProviderType.Msp, false, ProviderStatusType.Billable)]
    [BitAutoData(ProviderType.Msp, true, ProviderStatusType.Created)]
    public async Task IneligibleProvider_ReturnsForbidden(
        ProviderType type, bool enabled, ProviderStatusType status, SutProvider<ProviderController> sutProvider)
    {
        var provider = SetupProvider(sutProvider, type, enabled, status);
        SetupItems(sutProvider, provider.Id, BuildItems(provider.Id));

        AssertForbidden(await sutProvider.Sut.GetInvoices(new ProviderInvoiceFilterRequestModel()));
        AssertForbidden(await sutProvider.Sut.GetClientInvoices(_clientA, new ProviderInvoiceFilterRequestModel()));
        await sutProvider.GetDependency<IProviderInvoiceItemRepository>().DidNotReceiveWithAnyArgs().GetByProviderId(default);
    }

    [Theory, BitAutoData]
    public async Task ProviderNotFound_ReturnsForbidden(Guid providerId, SutProvider<ProviderController> sutProvider)
    {
        sutProvider.GetDependency<ICurrentContext>().ProviderId.Returns(providerId);

        AssertForbidden(await sutProvider.Sut.GetInvoices(new ProviderInvoiceFilterRequestModel()));
    }

    [Theory, BitAutoData]
    public async Task NoProviderIdInToken_ReturnsUnauthorized(SutProvider<ProviderController> sutProvider)
    {
        sutProvider.GetDependency<ICurrentContext>().ProviderId.Returns((Guid?)null);

        Assert.IsType<UnauthorizedResult>(await sutProvider.Sut.GetInvoices(new ProviderInvoiceFilterRequestModel()));
        Assert.IsType<UnauthorizedResult>(
            await sutProvider.Sut.GetClientInvoices(_clientA, new ProviderInvoiceFilterRequestModel()));
        await sutProvider.GetDependency<IProviderRepository>().DidNotReceiveWithAnyArgs().GetByIdAsync(default);
    }

    // Serialization

    [Theory, BitAutoData]
    public async Task Responses_NeverExposeInvoiceIdOrNumber(SutProvider<ProviderController> sutProvider)
    {
        var provider = SetupProvider(sutProvider);
        SetupItems(sutProvider, provider.Id, BuildItems(provider.Id));

        var invoices = await sutProvider.Sut.GetInvoices(new ProviderInvoiceFilterRequestModel
        {
            Start = "2026-01-01",
            End = "2026-12-31"
        });
        var clientInvoices = await sutProvider.Sut.GetClientInvoices(_clientA, new ProviderInvoiceFilterRequestModel
        {
            Start = "2026-01-01",
            End = "2026-12-31"
        });

        foreach (var result in new[] { invoices, clientInvoices })
        {
            var json = JsonSerializer.Serialize(Assert.IsType<JsonResult>(result).Value,
                new JsonSerializerOptions(JsonSerializerDefaults.Web));
            Assert.DoesNotContain("invoiceId", json, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("invoiceNumber", json, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("\"in_", json);
            Assert.DoesNotContain("INV-", json);
            Assert.DoesNotContain("Unassigned seats", json);
            Assert.Contains("\"object\":\"list\"", json);
            Assert.Contains("\"continuationToken\":null", json);
        }
    }

    [Theory]
    [BitAutoData(15.0000, "15.00")]
    [BitAutoData(12.345, "12.35")]
    [BitAutoData(12.3449, "12.34")]
    [BitAutoData(0, "0.00")]
    [BitAutoData(300, "300.00")]
    public async Task EstimatedTotal_IsRoundedToTwoDecimalPlaces(
        decimal storedTotal, string expectedJson, SutProvider<ProviderController> sutProvider)
    {
        var provider = SetupProvider(sutProvider);
        SetupItems(sutProvider, provider.Id,
        [
            Item(provider.Id, SeptemberInvoiceId, _clientA, _september, "Acme Corp", total: storedTotal),
            Item(provider.Id, SeptemberInvoiceId, null, _september, "Unassigned seats", total: storedTotal),
        ]);
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);

        var invoices = Assert.IsType<JsonResult>(await sutProvider.Sut.GetInvoices(new ProviderInvoiceFilterRequestModel()));
        var clientInvoices = Assert.IsType<JsonResult>(
            await sutProvider.Sut.GetClientInvoices(_clientA, new ProviderInvoiceFilterRequestModel()));

        var invoicesJson = JsonSerializer.Serialize(invoices.Value, options);
        Assert.Equal(2, CountOccurrences(invoicesJson, $"\"estimatedTotal\":{expectedJson}"));
        Assert.Contains($"\"estimatedTotal\":{expectedJson}", JsonSerializer.Serialize(clientInvoices.Value, options));
    }

    // Helpers

    private static int CountOccurrences(string value, string search) =>
        (value.Length - value.Replace(search, string.Empty).Length) / search.Length;

    private static Provider SetupProvider(
        SutProvider<ProviderController> sutProvider,
        ProviderType type = ProviderType.Msp,
        bool enabled = true,
        ProviderStatusType status = ProviderStatusType.Billable)
    {
        var provider = new Provider { Id = Guid.NewGuid(), Type = type, Enabled = enabled, Status = status };
        sutProvider.GetDependency<ICurrentContext>().ProviderId.Returns(provider.Id);
        sutProvider.GetDependency<IProviderRepository>().GetByIdAsync(provider.Id).Returns(provider);
        return provider;
    }

    private static void SetupItems(
        SutProvider<ProviderController> sutProvider, Guid providerId, ICollection<ProviderInvoiceItem> items) =>
        sutProvider.GetDependency<IProviderInvoiceItemRepository>().GetByProviderId(providerId).Returns(items);

    private static List<ProviderInvoiceItem> BuildItems(Guid providerId) =>
    [
        Item(providerId, SeptemberInvoiceId, null, _september, "Unassigned seats", 100, 0, 300.00m),
        Item(providerId, SeptemberInvoiceId, _clientB, _september, "Beta LLC"),
        Item(providerId, SeptemberInvoiceId, _clientA, _september, "Acme Corp", 25, 22, 75.00m),
        Item(providerId, OctoberInvoiceId, _clientA, _october, "Acme Corp", 30, 28, 90.00m),
        Item(providerId, OctoberInvoiceId, null, _october, "Unassigned seats", 95, 0, 285.00m),
    ];

    private static ProviderInvoiceItem Item(
        Guid providerId,
        string invoiceId,
        Guid? clientId,
        DateTime created,
        string clientName = "Client",
        int assignedSeats = 10,
        int usedSeats = 5,
        decimal total = 30.00m) => new()
        {
            Id = Guid.NewGuid(),
            ProviderId = providerId,
            InvoiceId = invoiceId,
            InvoiceNumber = $"INV-{invoiceId}",
            ClientId = clientId,
            ClientName = clientName,
            PlanName = "Enterprise (Monthly)",
            AssignedSeats = assignedSeats,
            UsedSeats = usedSeats,
            Total = total,
            Created = created,
        };

    private static async Task<PagedListResponseModel<ProviderInvoiceResponseModel>> GetInvoicesAsync(
        SutProvider<ProviderController> sutProvider, string? start, string? end)
    {
        var result = await sutProvider.Sut.GetInvoices(new ProviderInvoiceFilterRequestModel { Start = start, End = end });
        return Assert.IsType<PagedListResponseModel<ProviderInvoiceResponseModel>>(Assert.IsType<JsonResult>(result).Value);
    }

    private static async Task<PagedListResponseModel<ProviderClientInvoiceItemResponseModel>> GetClientInvoicesAsync(
        SutProvider<ProviderController> sutProvider, Guid organizationId, string? start, string? end)
    {
        var result = await sutProvider.Sut.GetClientInvoices(organizationId,
            new ProviderInvoiceFilterRequestModel { Start = start, End = end });
        return Assert.IsType<PagedListResponseModel<ProviderClientInvoiceItemResponseModel>>(
            Assert.IsType<JsonResult>(result).Value);
    }

    private static void AssertForbidden(IActionResult result)
    {
        var objectResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal((int)HttpStatusCode.Forbidden, objectResult.StatusCode);
        Assert.IsType<ErrorResponseModel>(objectResult.Value);
    }
}
