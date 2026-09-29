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
using Bit.Core.Enums;
using Bit.Core.Models.Data;
using Bit.Core.Repositories;
using Xunit;

namespace Bit.Api.IntegrationTest.Dirt.Public.Controllers;

public class ProviderEventsControllerTests : IAsyncLifetime
{
    // Features:FlagValues takes precedence over globalSettings:launchDarkly:flagValues (and over developer user
    // secrets, which the test hosts also load), so the flag state is deterministic.
    private const string EventsFlagSettingKey = $"Features:FlagValues:{FeatureFlagKeys.ProviderClientEvents}";
    private const string PublicApiFlagSettingKey = $"Features:FlagValues:{FeatureFlagKeys.ProviderPublicApi}";

    // Whole seconds, and distinct per event: on EF/SQLite, events sharing a timestamp can be skipped at page boundaries.
    private readonly DateTime _now = new(DateTime.UtcNow.Ticks / TimeSpan.TicksPerSecond * TimeSpan.TicksPerSecond, DateTimeKind.Utc);
    private int _seededEvents;
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
    public async Task GetEvents_PagesAcrossClientOrganizations()
    {
        CreateFactory(eventsFlagEnabled: true);
        var (provider, apiKey) = await CreateProviderAsync();
        var clients = await ProviderTestHelpers.CreateAndLinkClientOrganizationsAsync(_factory, provider.Id, 3);
        var noEvents = await ProviderTestHelpers.CreateAndLinkClientOrganizationsAsync(_factory, provider.Id, 1,
            useEvents: false);
        foreach (var client in clients.Concat(noEvents))
        {
            await SeedEventsAsync(client.Id, 30, providerId: provider.Id);
        }

        await LoginAsProviderAsync(provider.Id, apiKey);

        var pages = await ReadAllPagesAsync("/public/provider/events");

        Assert.Equal(2, pages.Count);
        Assert.Equal(50, pages[0].Count);
        var events = pages.SelectMany(p => p).ToList();
        Assert.Equal(90, events.Count);
        // Org-major: each client's events are contiguous and newest first, clients in ID order.
        var chunks = events.Chunk(30).ToList();
        Assert.All(chunks, chunk => Assert.Single(chunk.Select(e => e.OrganizationId).Distinct()));
        Assert.Equal(clients.Select(c => c.Id).Order(), chunks.Select(chunk => chunk[0].OrganizationId));
        Assert.All(chunks, chunk => Assert.Equal(chunk.OrderByDescending(e => e.Date), chunk));
        Assert.DoesNotContain(events, e => e.OrganizationId == noEvents[0].Id);
        Assert.All(events, e => Assert.Equal(provider.Id, e.ProviderId));
    }

    [Fact]
    public async Task GetClientEvents_PagesOneClientNewestFirst()
    {
        CreateFactory(eventsFlagEnabled: true);
        var (provider, apiKey) = await CreateProviderAsync();
        var clients = await ProviderTestHelpers.CreateAndLinkClientOrganizationsAsync(_factory, provider.Id, 2);
        await SeedEventsAsync(clients[0].Id, 60);
        await SeedEventsAsync(clients[1].Id, 5);
        await LoginAsProviderAsync(provider.Id, apiKey);

        var pages = await ReadAllPagesAsync($"/public/provider/clients/{clients[0].Id}/events");

        Assert.Equal(new[] { 50, 10 }, pages.Select(p => p.Count));
        var events = pages.SelectMany(p => p).ToList();
        Assert.All(events, e =>
        {
            Assert.Equal(clients[0].Id, e.OrganizationId);
            Assert.Null(e.ProviderId);
        });
        Assert.Equal(events.OrderByDescending(e => e.Date).Select(e => e.Date), events.Select(e => e.Date));
        Assert.Equal(60, events.Select(e => e.Date).Distinct().Count());
    }

    [Fact]
    public async Task AggregateContinuationToken_IsRejectedByClientEndpoint()
    {
        CreateFactory(eventsFlagEnabled: true);
        var (provider, apiKey) = await CreateProviderAsync();
        var clients = await ProviderTestHelpers.CreateAndLinkClientOrganizationsAsync(_factory, provider.Id, 1);
        await SeedEventsAsync(clients[0].Id, 60);
        await LoginAsProviderAsync(provider.Id, apiKey);

        var (_, token) = await GetPageAsync("/public/provider/events");
        Assert.NotNull(token);

        var response = await _client.GetAsync(
            $"/public/provider/clients/{clients[0].Id}/events?continuationToken={Uri.EscapeDataString(token)}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task ProviderKey_NeverReturnsAnotherProvidersClients()
    {
        CreateFactory(eventsFlagEnabled: true);
        var (providerA, apiKeyA) = await CreateProviderAsync();
        var (providerB, _) = await CreateProviderAsync();
        var clientsA = await ProviderTestHelpers.CreateAndLinkClientOrganizationsAsync(_factory, providerA.Id, 1);
        var clientsB = await ProviderTestHelpers.CreateAndLinkClientOrganizationsAsync(_factory, providerB.Id, 1);
        await SeedEventsAsync(clientsA[0].Id, 5);
        await SeedEventsAsync(clientsB[0].Id, 5);
        await LoginAsProviderAsync(providerA.Id, apiKeyA);

        var events = (await ReadAllPagesAsync("/public/provider/events")).SelectMany(p => p).ToList();

        Assert.Equal(5, events.Count);
        Assert.All(events, e => Assert.Equal(clientsA[0].Id, e.OrganizationId));

        var otherClient = await _client.GetAsync($"/public/provider/clients/{clientsB[0].Id}/events");
        Assert.Equal(HttpStatusCode.NotFound, otherClient.StatusCode);
    }

    [Fact]
    public async Task OrganizationApiKey_ReturnsForbidden()
    {
        CreateFactory(eventsFlagEnabled: true);
        var ownerEmail = $"integration-test{Guid.NewGuid()}@bitwarden.com";
        await _factory.LoginWithNewAccount(ownerEmail);
        var (organization, _) = await OrganizationTestHelpers.SignUpAsync(_factory, plan: PlanType.EnterpriseAnnually,
            ownerEmail: ownerEmail, passwordManagerSeats: 10, paymentMethod: PaymentMethodType.Card);
        await new LoginHelper(_factory, _client).LoginWithOrganizationApiKeyAsync(organization.Id);

        var events = await _client.GetAsync("/public/provider/events");
        var clientEvents = await _client.GetAsync($"/public/provider/clients/{organization.Id}/events");

        Assert.Equal(HttpStatusCode.Forbidden, events.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, clientEvents.StatusCode);
    }

    [Fact]
    public async Task FeatureFlagDisabled_ReturnsNotFound()
    {
        CreateFactory(eventsFlagEnabled: false);
        var (provider, apiKey) = await CreateProviderAsync();
        var clients = await ProviderTestHelpers.CreateAndLinkClientOrganizationsAsync(_factory, provider.Id, 1);
        await LoginAsProviderAsync(provider.Id, apiKey);

        var events = await _client.GetAsync("/public/provider/events");
        var clientEvents = await _client.GetAsync($"/public/provider/clients/{clients[0].Id}/events");

        Assert.Equal(HttpStatusCode.NotFound, events.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, clientEvents.StatusCode);
    }

    private void CreateFactory(bool eventsFlagEnabled)
    {
        _factory = new ApiApplicationFactory();
        _factory.UpdateConfiguration(EventsFlagSettingKey, eventsFlagEnabled ? "true" : "false");
        // Identity only issues provider tokens with the provider public API flag on.
        _factory.Identity.UpdateConfiguration(PublicApiFlagSettingKey, "true");
        _client = _factory.CreateClient();
    }

    private async Task<(Provider Provider, string ApiKey)> CreateProviderAsync()
    {
        var provider = await _factory.GetService<IProviderRepository>().CreateAsync(new Provider
        {
            Name = "Test Provider",
            BillingEmail = "billing@example.com",
            Type = ProviderType.Msp,
            Status = ProviderStatusType.Billable,
            Enabled = true,
        });

        var apiKey = await _factory.GetService<IProviderApiKeyRepository>().CreateAsync(new ProviderApiKey
        {
            ProviderId = provider.Id,
            Type = ProviderApiKeyType.Default,
            ApiKey = $"provider-test-key-{Guid.NewGuid():N}"[..30],
            RevisionDate = DateTime.UtcNow,
        });

        return (provider, apiKey.ApiKey);
    }

    private async Task SeedEventsAsync(Guid organizationId, int count, Guid? providerId = null)
    {
        var events = Enumerable.Range(0, count).Select(_ => (IEvent)new EventMessage
        {
            Type = EventType.Cipher_Created,
            OrganizationId = organizationId,
            ProviderId = providerId,
            Date = _now.AddSeconds(-++_seededEvents),
        }).ToList();
        await _factory.GetService<IEventRepository>().CreateManyAsync(events);
    }

    private async Task LoginAsProviderAsync(Guid providerId, string apiKey)
    {
        var token = await _factory.LoginWithProviderApiKeyAsync(providerId, apiKey);
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
    }

    private async Task<List<List<EventResult>>> ReadAllPagesAsync(string path)
    {
        var pages = new List<List<EventResult>>();
        string? token = null;
        do
        {
            Assert.True(pages.Count < 20, "Paging did not terminate.");
            var separator = path.Contains('?') ? '&' : '?';
            var (events, next) = await GetPageAsync(token == null
                ? path
                : $"{path}{separator}continuationToken={Uri.EscapeDataString(token)}");
            pages.Add(events);
            token = next;
        } while (token != null);

        return pages;
    }

    private async Task<(List<EventResult> Events, string? ContinuationToken)> GetPageAsync(string path)
    {
        var response = await _client.GetAsync(path);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("list", doc.RootElement.GetProperty("object").GetString());
        var events = doc.RootElement.GetProperty("data").EnumerateArray()
            .Select(e => new EventResult(
                e.GetProperty("organizationId").GetGuid(),
                e.GetProperty("providerId").ValueKind == JsonValueKind.Null ? null : e.GetProperty("providerId").GetGuid(),
                e.GetProperty("date").GetDateTime()))
            .ToList();
        return (events, doc.RootElement.GetProperty("continuationToken").GetString());
    }

    private record EventResult(Guid OrganizationId, Guid? ProviderId, DateTime Date);
}
