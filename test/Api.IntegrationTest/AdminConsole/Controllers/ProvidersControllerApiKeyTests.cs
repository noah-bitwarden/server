using System.Net;
using System.Text.Json;
using Bit.Api.IntegrationTest.Factories;
using Bit.Api.IntegrationTest.Helpers;
using Bit.Core;
using Bit.Core.AdminConsole.Entities.Provider;
using Bit.Core.AdminConsole.Enums.Provider;
using Bit.Core.AdminConsole.Repositories;
using Xunit;

namespace Bit.Api.IntegrationTest.AdminConsole.Controllers;

public class ProvidersControllerApiKeyTests : IAsyncLifetime
{
    // Features:FlagValues takes precedence over globalSettings:launchDarkly:flagValues (and over developer user
    // secrets, which the test hosts also load), so the flag state is deterministic.
    private const string FlagSettingKey = $"Features:FlagValues:{FeatureFlagKeys.ProviderApiKey}";
    private const string MasterPasswordHash = "master_password_hash";

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
    public async Task ApiKey_MspProviderAdmin_CreatesAndReturnsKey()
    {
        CreateFactory(flagEnabled: true);
        var provider = await CreateProviderAsync(ProviderType.Msp);
        await LoginAsProviderUserAsync(provider.Id, ProviderUserType.ProviderAdmin);

        var response = await PostAsync(provider.Id, "api-key", type: 0, MasterPasswordHash);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("apiKey", body.RootElement.GetProperty("object").GetString());
        var apiKey = body.RootElement.GetProperty("apiKey").GetString();
        Assert.Equal(30, apiKey!.Length);
        var storedKey = Assert.Single(await GetStoredKeysAsync(provider.Id));
        Assert.Equal(apiKey, storedKey.ApiKey);
    }

    [Theory]
    [InlineData("api-key")]
    [InlineData("rotate-api-key")]
    public async Task UndefinedType_ReturnsBadRequestBeforeSecretVerification(string route)
    {
        CreateFactory(flagEnabled: true);
        var provider = await CreateProviderAsync(ProviderType.Msp);
        await LoginAsProviderUserAsync(provider.Id, ProviderUserType.ProviderAdmin);

        // A wrong secret would produce a MasterPasswordHash error if secret verification ran
        var response = await PostAsync(provider.Id, route, type: 1, "wrong-master-password-hash");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var errorKeys = await GetValidationErrorKeysAsync(response);
        Assert.Contains("Type", errorKeys, StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain("MasterPasswordHash", errorKeys, StringComparer.OrdinalIgnoreCase);
        Assert.Empty(await GetStoredKeysAsync(provider.Id));
    }

    [Fact]
    public async Task ApiKey_BusinessUnitProvider_ReturnsNotFoundAndCreatesNoKey()
    {
        CreateFactory(flagEnabled: true);
        var provider = await CreateProviderAsync(ProviderType.BusinessUnit);
        await LoginAsProviderUserAsync(provider.Id, ProviderUserType.ProviderAdmin);

        var response = await PostAsync(provider.Id, "api-key", type: 0, MasterPasswordHash);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Empty(await GetStoredKeysAsync(provider.Id));
    }

    [Fact]
    public async Task RotateApiKey_BusinessUnitProviderWithExistingKey_ReturnsNotFoundAndDoesNotRotate()
    {
        CreateFactory(flagEnabled: true);
        var provider = await CreateProviderAsync(ProviderType.BusinessUnit);
        var existingKey = await _factory.GetService<IProviderApiKeyRepository>().CreateAsync(new ProviderApiKey
        {
            ProviderId = provider.Id,
            Type = ProviderApiKeyType.BillingReadOnly,
            ApiKey = "business-unit-existing-key-000",
            RevisionDate = DateTime.UtcNow,
        });
        await LoginAsProviderUserAsync(provider.Id, ProviderUserType.ProviderAdmin);

        var response = await PostAsync(provider.Id, "rotate-api-key", type: 0, MasterPasswordHash);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var storedKey = Assert.Single(await GetStoredKeysAsync(provider.Id));
        Assert.Equal(existingKey.ApiKey, storedKey.ApiKey);
    }

    [Theory]
    [InlineData("api-key")]
    [InlineData("rotate-api-key")]
    public async Task ServiceUser_ReturnsForbidden(string route)
    {
        CreateFactory(flagEnabled: true);
        var provider = await CreateProviderAsync(ProviderType.Msp);
        await LoginAsProviderUserAsync(provider.Id, ProviderUserType.ServiceUser);

        var response = await PostAsync(provider.Id, route, type: 0, MasterPasswordHash);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Empty(await GetStoredKeysAsync(provider.Id));
    }

    [Theory]
    [InlineData("api-key")]
    [InlineData("rotate-api-key")]
    public async Task FlagOff_ReturnsNotFound(string route)
    {
        CreateFactory(flagEnabled: false);
        var provider = await CreateProviderAsync(ProviderType.Msp);
        await LoginAsProviderUserAsync(provider.Id, ProviderUserType.ProviderAdmin);

        var response = await PostAsync(provider.Id, route, type: 0, MasterPasswordHash);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Empty(await GetStoredKeysAsync(provider.Id));
    }

    private void CreateFactory(bool flagEnabled)
    {
        _factory = new ApiApplicationFactory();
        _factory.UpdateConfiguration(FlagSettingKey, flagEnabled ? "true" : "false");
        _client = _factory.CreateClient();
    }

    private async Task<Provider> CreateProviderAsync(ProviderType type) =>
        await _factory.GetService<IProviderRepository>().CreateAsync(new Provider
        {
            Name = "Test Provider",
            BillingEmail = "billing@example.com",
            Type = type,
            Status = ProviderStatusType.Billable,
            Enabled = true,
        });

    private async Task LoginAsProviderUserAsync(Guid providerId, ProviderUserType type)
    {
        var email = $"{Guid.NewGuid()}@example.com";
        await _factory.LoginWithNewAccount(email, MasterPasswordHash);
        await ProviderTestHelpers.CreateProviderUserAsync(_factory, providerId, email, type);
        // Log in again so the token carries the provider membership claims
        await new LoginHelper(_factory, _client).LoginAsync(email);
    }

    private Task<HttpResponseMessage> PostAsync(Guid providerId, string route, int type, string masterPasswordHash) =>
        _client.PostAsync($"/providers/{providerId}/{route}",
            JsonContent.Create(new { type, masterPasswordHash }));

    private async Task<List<ProviderApiKey>> GetStoredKeysAsync(Guid providerId) =>
        (await _factory.GetService<IProviderApiKeyRepository>().GetManyByProviderIdTypeAsync(providerId)).ToList();

    private static async Task<List<string>> GetValidationErrorKeysAsync(HttpResponseMessage response)
    {
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return body.RootElement.GetProperty("validationErrors").EnumerateObject().Select(p => p.Name).ToList();
    }
}
