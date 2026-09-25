using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Bit.Api.IntegrationTest.Factories;
using Bit.Api.IntegrationTest.Helpers;
using Bit.Core;
using Bit.Core.AdminConsole.Entities.Provider;
using Bit.Core.AdminConsole.Enums.Provider;
using Bit.Core.AdminConsole.Repositories;
using Bit.Core.Billing.Enums;
using Bit.Core.Billing.Providers.Entities;
using Bit.Core.Billing.Providers.Repositories;
using Bit.Core.Enums;
using Xunit;

namespace Bit.Api.IntegrationTest.Billing.Public.Controllers;

public class ProviderControllerTests : IAsyncLifetime
{
    // Features:FlagValues takes precedence over globalSettings:launchDarkly:flagValues (and over developer user
    // secrets, which the test hosts also load), so the flag state is deterministic.
    private const string FlagSettingKey = $"Features:FlagValues:{FeatureFlagKeys.ProviderPublicApi}";

    private readonly Guid _clientOrganizationId = Guid.NewGuid();
    private ApiApplicationFactory _factory = null!;
    private HttpClient _client = null!;

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        _client?.Dispose();
        if (_factory != null)
        {
            await _factory.DisposeAsync();
        }
    }

    [Fact]
    public async Task ProviderToken_ReturnsInvoices_AndEnforcesOwnershipAndValidation()
    {
        CreateFactory(apiFlagEnabled: true);
        var (provider, apiKey) = await CreateProviderAsync(ProviderType.Msp);
        await SeedInvoiceItemsAsync(provider.Id);
        await LoginAsProviderAsync(provider.Id, apiKey);

        var invoices = await _client.GetAsync("/public/provider/invoices?start=2026-09-01&end=2026-09-30");
        Assert.Equal(HttpStatusCode.OK, invoices.StatusCode);
        var invoicesJson = await invoices.Content.ReadAsStringAsync();
        using (var doc = JsonDocument.Parse(invoicesJson))
        {
            Assert.Equal("list", doc.RootElement.GetProperty("object").GetString());
            var invoice = Assert.Single(doc.RootElement.GetProperty("data").EnumerateArray());
            Assert.Equal("providerInvoice", invoice.GetProperty("object").GetString());
            Assert.Equal("2026-09-01T00:00:00Z", invoice.GetProperty("invoiceDate").GetString());
            var lineItems = invoice.GetProperty("lineItems").EnumerateArray().ToList();
            Assert.Equal(2, lineItems.Count);
            Assert.Equal("client", lineItems[0].GetProperty("type").GetString());
            Assert.Equal(3, lineItems[0].GetProperty("remainingSeats").GetInt32());
            Assert.Equal(75.00m, lineItems[0].GetProperty("estimatedTotal").GetDecimal());
            Assert.Equal("unassigned", lineItems[1].GetProperty("type").GetString());
            Assert.Equal(JsonValueKind.Null, lineItems[1].GetProperty("organizationId").ValueKind);
            Assert.Equal(JsonValueKind.Null, lineItems[1].GetProperty("clientName").ValueKind);
        }
        AssertNoInvoiceIdentifiers(invoicesJson);

        var clientInvoices = await _client.GetAsync($"/public/provider/clients/{_clientOrganizationId}/invoices");
        Assert.Equal(HttpStatusCode.OK, clientInvoices.StatusCode);
        var clientJson = await clientInvoices.Content.ReadAsStringAsync();
        using (var doc = JsonDocument.Parse(clientJson))
        {
            // No dates: only the most recent line item
            var item = Assert.Single(doc.RootElement.GetProperty("data").EnumerateArray());
            Assert.Equal("providerClientInvoiceItem", item.GetProperty("object").GetString());
            Assert.Equal(_clientOrganizationId, item.GetProperty("organizationId").GetGuid());
            Assert.Equal("2026-10-01T00:00:00Z", item.GetProperty("invoiceDate").GetString());
        }
        AssertNoInvoiceIdentifiers(clientJson);

        // The same invoice has the same invoiceDate on both endpoints.
        var clientSeptember = await _client.GetAsync(
            $"/public/provider/clients/{_clientOrganizationId}/invoices?start=2026-09-01&end=2026-09-30");
        Assert.Equal(HttpStatusCode.OK, clientSeptember.StatusCode);
        using (var doc = JsonDocument.Parse(await clientSeptember.Content.ReadAsStringAsync()))
        {
            var item = Assert.Single(doc.RootElement.GetProperty("data").EnumerateArray());
            Assert.Equal("2026-09-01T00:00:00Z", item.GetProperty("invoiceDate").GetString());
        }

        var notMine = await _client.GetAsync($"/public/provider/clients/{Guid.NewGuid()}/invoices");
        Assert.Equal(HttpStatusCode.NotFound, notMine.StatusCode);

        var outOfRange = await _client.GetAsync(
            $"/public/provider/clients/{_clientOrganizationId}/invoices?start=2025-01-01&end=2025-01-31");
        Assert.Equal(HttpStatusCode.OK, outOfRange.StatusCode);
        using (var doc = JsonDocument.Parse(await outOfRange.Content.ReadAsStringAsync()))
        {
            Assert.Empty(doc.RootElement.GetProperty("data").EnumerateArray());
        }

        await AssertBadRequestAsync("/public/provider/invoices?start=2026-10-01&end=2026-09-01",
            "The start date must not be after the end date.");
        await AssertBadRequestAsync("/public/provider/invoices?start=20260901",
            "The start date must be a valid date in yyyy-MM-dd format.");
        await AssertBadRequestAsync($"/public/provider/clients/{_clientOrganizationId}/invoices?end=2026-02-30",
            "The end date must be a valid date in yyyy-MM-dd format.");
    }

    [Fact]
    public async Task ProviderToken_CannotAccessProviderApiKeyEndpoints()
    {
        CreateFactory(apiFlagEnabled: true);
        _factory.UpdateConfiguration($"Features:FlagValues:{FeatureFlagKeys.ProviderApiKey}", "true");
        var (provider, apiKey) = await CreateProviderAsync(ProviderType.Msp);
        await LoginAsProviderAsync(provider.Id, apiKey);

        foreach (var route in new[] { "api-key", "rotate-api-key" })
        {
            var response = await _client.PostAsync($"/providers/{provider.Id}/{route}",
                JsonContent.Create(new { type = 0, masterPasswordHash = "x" }));

            Assert.Contains(response.StatusCode, new[] { HttpStatusCode.Unauthorized, HttpStatusCode.Forbidden });
            Assert.DoesNotContain(apiKey, await response.Content.ReadAsStringAsync());
        }
    }

    [Fact]
    public async Task NonMspProvider_ReturnsForbidden()
    {
        CreateFactory(apiFlagEnabled: true);
        var (provider, apiKey) = await CreateProviderAsync(ProviderType.BusinessUnit);
        await SeedInvoiceItemsAsync(provider.Id);
        await LoginAsProviderAsync(provider.Id, apiKey);

        var response = await _client.GetAsync("/public/provider/invoices");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task FeatureFlagDisabled_ReturnsNotFound()
    {
        CreateFactory(apiFlagEnabled: false);
        var (provider, apiKey) = await CreateProviderAsync(ProviderType.Msp);
        await SeedInvoiceItemsAsync(provider.Id);
        await LoginAsProviderAsync(provider.Id, apiKey);

        var response = await _client.GetAsync("/public/provider/invoices");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task OrganizationToken_ReturnsForbidden()
    {
        CreateFactory(apiFlagEnabled: true);
        var ownerEmail = $"integration-test{Guid.NewGuid()}@bitwarden.com";
        await _factory.LoginWithNewAccount(ownerEmail);
        var (organization, _) = await OrganizationTestHelpers.SignUpAsync(_factory, plan: PlanType.EnterpriseAnnually,
            ownerEmail: ownerEmail, passwordManagerSeats: 10, paymentMethod: PaymentMethodType.Card);
        await new LoginHelper(_factory, _client).LoginWithOrganizationApiKeyAsync(organization.Id);

        var response = await _client.GetAsync("/public/provider/invoices");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    private void CreateFactory(bool apiFlagEnabled)
    {
        _factory = new ApiApplicationFactory();
        _factory.UpdateConfiguration(FlagSettingKey, apiFlagEnabled ? "true" : "false");
        _factory.Identity.UpdateConfiguration(FlagSettingKey, "true");
        _client = _factory.CreateClient();
    }

    private async Task<(Provider Provider, string ApiKey)> CreateProviderAsync(ProviderType type)
    {
        var provider = await _factory.GetService<IProviderRepository>().CreateAsync(new Provider
        {
            Name = "Test Provider",
            BillingEmail = "billing@example.com",
            Type = type,
            Status = ProviderStatusType.Billable,
            Enabled = true,
        });

        var apiKey = await _factory.GetService<IProviderApiKeyRepository>().CreateAsync(new ProviderApiKey
        {
            ProviderId = provider.Id,
            Type = ProviderApiKeyType.BillingReadOnly,
            ApiKey = "provider-test-api-key-0123456",
            RevisionDate = DateTime.UtcNow,
        });

        return (provider, apiKey.ApiKey);
    }

    private async Task SeedInvoiceItemsAsync(Guid providerId)
    {
        var repository = _factory.GetService<IProviderInvoiceItemRepository>();
        var september = new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc);
        var october = new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc);

        // The client's row is recorded after the unassigned row, so it is not the invoice's earliest row.
        await repository.CreateAsync(Item(providerId, "in_test_september", _clientOrganizationId, "Acme Corp", 25, 22, 75.00m, september.AddMilliseconds(750)));
        await repository.CreateAsync(Item(providerId, "in_test_september", null, "Unassigned seats", 100, 0, 300.00m, september));
        await repository.CreateAsync(Item(providerId, "in_test_october", _clientOrganizationId, "Acme Corp", 30, 28, 90.00m, october));
    }

    private static ProviderInvoiceItem Item(Guid providerId, string invoiceId, Guid? clientId, string clientName,
        int assigned, int used, decimal total, DateTime created) => new()
        {
            ProviderId = providerId,
            InvoiceId = invoiceId,
            InvoiceNumber = $"INV-{invoiceId}",
            ClientId = clientId,
            ClientName = clientName,
            PlanName = "Enterprise (Monthly)",
            AssignedSeats = assigned,
            UsedSeats = used,
            Total = total,
            Created = created,
        };

    private async Task LoginAsProviderAsync(Guid providerId, string apiKey)
    {
        var token = await _factory.LoginWithProviderApiKeyAsync(providerId, apiKey);
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
    }

    private async Task AssertBadRequestAsync(string path, string expectedMessage)
    {
        var response = await _client.GetAsync(path);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("error", doc.RootElement.GetProperty("object").GetString());
        Assert.Equal(expectedMessage, doc.RootElement.GetProperty("message").GetString());
        Assert.Equal(JsonValueKind.Null, doc.RootElement.GetProperty("errors").ValueKind);
    }

    private static void AssertNoInvoiceIdentifiers(string json)
    {
        Assert.DoesNotContain("invoiceId", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("invoiceNumber", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("\"in_", json);
        Assert.DoesNotContain("INV-", json);
        Assert.DoesNotContain("Unassigned seats", json);
    }
}
