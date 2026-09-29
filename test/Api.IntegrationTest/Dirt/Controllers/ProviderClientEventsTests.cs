using System.Net;
using System.Text.Json;
using Bit.Api.IntegrationTest.Factories;
using Bit.Api.IntegrationTest.Helpers;
using Bit.Core;
using Bit.Core.AdminConsole.Entities.Provider;
using Bit.Core.AdminConsole.Enums.Provider;
using Bit.Core.AdminConsole.Repositories;
using Bit.Core.Enums;
using Bit.Core.Models.Data;
using Bit.Core.Repositories;
using Xunit;

namespace Bit.Api.IntegrationTest.Dirt.Controllers;

/// <summary>
/// Integration tests for <c>GET /providers/{providerId}/client-events</c>.
/// </summary>
public class ProviderClientEventsTests : IAsyncLifetime
{
    // Features:FlagValues takes precedence over globalSettings:launchDarkly:flagValues (and over developer user
    // secrets, which the test hosts also load), so the flag state is deterministic.
    private const string FlagSettingKey = $"Features:FlagValues:{FeatureFlagKeys.ProviderClientEvents}";

    private readonly DateTime _now = new(DateTime.UtcNow.Ticks / TimeSpan.TicksPerSecond * TimeSpan.TicksPerSecond, DateTimeKind.Utc);
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
    public async Task ProviderAdmin_PagesMergedEventsForSelectedClients()
    {
        CreateFactory(flagEnabled: true);
        var provider = await CreateProviderAsync();
        var clients = await ProviderTestHelpers.CreateAndLinkClientOrganizationsAsync(_factory, provider.Id, 3);
        // Distinct, interleaved timestamps: on EF/SQLite, events sharing a timestamp can be skipped at page boundaries.
        var events = Enumerable.Range(1, 90).Select(i => (IEvent)new EventMessage
        {
            Type = EventType.Cipher_Created,
            OrganizationId = clients[i % 3].Id,
            Date = _now.AddSeconds(-i),
        }).ToList();
        await _factory.GetService<IEventRepository>().CreateManyAsync(events);
        await LoginAsProviderUserAsync(provider.Id, ProviderUserType.ProviderAdmin);

        var basePath = $"/providers/{provider.Id}/client-events?organizationId={clients[0].Id}&organizationId={clients[1].Id}";
        var results = new List<(Guid OrganizationId, DateTime Date)>();
        string? token = null;
        var pages = 0;
        do
        {
            var path = token == null ? basePath : $"{basePath}&continuationToken={Uri.EscapeDataString(token)}";
            var response = await _client.GetAsync(path);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            results.AddRange(doc.RootElement.GetProperty("data").EnumerateArray()
                .Select(e => (e.GetProperty("organizationId").GetGuid(), e.GetProperty("date").GetDateTime())));
            token = doc.RootElement.GetProperty("continuationToken").GetString();
            pages++;
        } while (token != null && pages < 10);

        Assert.Null(token);
        var expected = events
            .Where(e => e.OrganizationId == clients[0].Id || e.OrganizationId == clients[1].Id)
            .Select(e => (e.OrganizationId!.Value, e.Date))
            .ToList();
        Assert.Equal(60, results.Count);
        Assert.Equal(expected.Select(e => e.Value), results.Select(r => r.OrganizationId));
        Assert.Equal(expected.Select(e => e.Date), results.Select(r => r.Date));
    }

    [Fact]
    public async Task ServiceUser_ReturnsNotFound()
    {
        CreateFactory(flagEnabled: true);
        var provider = await CreateProviderAsync();
        var clients = await ProviderTestHelpers.CreateAndLinkClientOrganizationsAsync(_factory, provider.Id, 1);
        await _factory.GetService<IEventRepository>().CreateManyAsync(Enumerable.Range(1, 3)
            .Select(i => (IEvent)new EventMessage
            {
                Type = EventType.Cipher_Created,
                OrganizationId = clients[0].Id,
                Date = _now.AddSeconds(-i),
            }).ToList());
        await LoginAsProviderUserAsync(provider.Id, ProviderUserType.ServiceUser);

        var response = await _client.GetAsync($"/providers/{provider.Id}/client-events");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.DoesNotContain(clients[0].Id.ToString(), await response.Content.ReadAsStringAsync());
    }

    [Theory]
    [InlineData(ProviderUserType.ProviderAdmin)]
    [InlineData(ProviderUserType.ServiceUser)]
    public async Task UserOfAnotherProvider_ReturnsNotFound(ProviderUserType type)
    {
        CreateFactory(flagEnabled: true);
        var provider = await CreateProviderAsync();
        var otherProvider = await CreateProviderAsync();
        await ProviderTestHelpers.CreateAndLinkClientOrganizationsAsync(_factory, provider.Id, 1);
        await LoginAsProviderUserAsync(otherProvider.Id, type);

        var response = await _client.GetAsync($"/providers/{provider.Id}/client-events");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task MoreThanTenClients_ReturnsBadRequest()
    {
        CreateFactory(flagEnabled: true);
        var provider = await CreateProviderAsync();
        var clients = await ProviderTestHelpers.CreateAndLinkClientOrganizationsAsync(_factory, provider.Id, 11);
        await LoginAsProviderUserAsync(provider.Id, ProviderUserType.ProviderAdmin);

        var unfiltered = await _client.GetAsync($"/providers/{provider.Id}/client-events");
        var filter = string.Join('&', clients.Select(c => $"organizationId={c.Id}"));
        var filtered = await _client.GetAsync($"/providers/{provider.Id}/client-events?{filter}");

        Assert.Equal(HttpStatusCode.BadRequest, unfiltered.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, filtered.StatusCode);
    }

    [Fact]
    public async Task FeatureFlagDisabled_ReturnsNotFound()
    {
        CreateFactory(flagEnabled: false);
        var provider = await CreateProviderAsync();
        await LoginAsProviderUserAsync(provider.Id, ProviderUserType.ProviderAdmin);

        var response = await _client.GetAsync($"/providers/{provider.Id}/client-events");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private void CreateFactory(bool flagEnabled)
    {
        _factory = new ApiApplicationFactory();
        _factory.UpdateConfiguration(FlagSettingKey, flagEnabled ? "true" : "false");
        _client = _factory.CreateClient();
    }

    private async Task<Provider> CreateProviderAsync() =>
        await _factory.GetService<IProviderRepository>().CreateAsync(new Provider
        {
            Name = "Test Provider",
            BillingEmail = "billing@example.com",
            Type = ProviderType.Msp,
            Status = ProviderStatusType.Billable,
            Enabled = true,
        });

    private async Task LoginAsProviderUserAsync(Guid providerId, ProviderUserType type)
    {
        var email = $"{Guid.NewGuid()}@example.com";
        await _factory.LoginWithNewAccount(email);
        await ProviderTestHelpers.CreateProviderUserAsync(_factory, providerId, email, type);
        // Log in again so the token carries the provider membership claims
        await new LoginHelper(_factory, _client).LoginAsync(email);
    }
}
