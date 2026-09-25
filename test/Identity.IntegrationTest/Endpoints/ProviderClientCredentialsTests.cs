using System.Text.Json;
using Bit.Core;
using Bit.Core.AdminConsole.Entities.Provider;
using Bit.Core.AdminConsole.Enums.Provider;
using Bit.Core.AdminConsole.Repositories;
using Bit.Core.Auth.IdentityServer;
using Bit.IntegrationTestCommon.Factories;
using Bit.Test.Common.Helpers;
using Xunit;

namespace Bit.Identity.IntegrationTest.Endpoints;

/// <summary>
/// Client credentials token requests for Provider API keys (<c>provider.{providerId}</c>).
/// </summary>
public class ProviderClientCredentialsTests
{
    // Features:FlagValues takes precedence over globalSettings:launchDarkly:flagValues (and over developer user
    // secrets, which the test hosts also load), so the flag state is deterministic.
    private const string FlagSettingKey = $"Features:FlagValues:{FeatureFlagKeys.ProviderPublicApi}";

    [Fact]
    public async Task TokenEndpoint_AsProvider_Success()
    {
        var factory = CreateFactory(flagEnabled: true);
        var (provider, apiKey) = await CreateProviderWithApiKeyAsync(factory);

        var context = await PostTokenAsync(factory, $"provider.{provider.Id}", apiKey.ApiKey);

        Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
        using var body = await AssertHelper.AssertResponseTypeIs<JsonDocument>(context);
        var root = body.RootElement;
        Assert.Equal(ApiScopes.ApiProvider,
            AssertHelper.AssertJsonProperty(root, "scope", JsonValueKind.String).GetString());
        Assert.Equal(3600, AssertHelper.AssertJsonProperty(root, "expires_in", JsonValueKind.Number).GetInt32());

        var token = AssertHelper.AssertJsonProperty(root, "access_token", JsonValueKind.String).GetString();
        var claims = ReadJwtPayload(token!);
        Assert.Equal($"provider.{provider.Id}", claims.GetProperty("client_id").GetString());
        Assert.Equal(provider.Id.ToString(), claims.GetProperty("client_sub").GetString());
        Assert.Contains(ApiScopes.ApiProvider, ReadScopes(claims));
    }

    [Fact]
    public async Task TokenEndpoint_AsProvider_WrongSecret_Fails()
    {
        var factory = CreateFactory(flagEnabled: true);
        var (provider, _) = await CreateProviderWithApiKeyAsync(factory);

        var context = await PostTokenAsync(factory, $"provider.{provider.Id}", "wrong-secret");

        await AssertInvalidClientAsync(context);
    }

    [Fact]
    public async Task TokenEndpoint_AsProvider_UnknownProvider_Fails()
    {
        var factory = CreateFactory(flagEnabled: true);

        var context = await PostTokenAsync(factory, $"provider.{Guid.NewGuid()}", "something");

        await AssertInvalidClientAsync(context);
    }

    [Fact]
    public async Task TokenEndpoint_AsProvider_FeatureFlagDisabled_Fails()
    {
        var factory = CreateFactory(flagEnabled: false);
        var (provider, apiKey) = await CreateProviderWithApiKeyAsync(factory);

        var context = await PostTokenAsync(factory, $"provider.{provider.Id}", apiKey.ApiKey);

        await AssertInvalidClientAsync(context);
    }

    [Fact]
    public async Task TokenEndpoint_AsProvider_DisabledProvider_Fails()
    {
        var factory = CreateFactory(flagEnabled: true);
        var (provider, apiKey) = await CreateProviderWithApiKeyAsync(factory, enabled: false);

        var context = await PostTokenAsync(factory, $"provider.{provider.Id}", apiKey.ApiKey);

        await AssertInvalidClientAsync(context);
    }

    private static IdentityApplicationFactory CreateFactory(bool flagEnabled)
    {
        var factory = new IdentityApplicationFactory();
        factory.UpdateConfiguration(FlagSettingKey, flagEnabled ? "true" : "false");
        return factory;
    }

    private static async Task<(Provider, ProviderApiKey)> CreateProviderWithApiKeyAsync(
        IdentityApplicationFactory factory, bool enabled = true)
    {
        var provider = await factory.Services.GetRequiredService<IProviderRepository>().CreateAsync(new Provider
        {
            Name = "Test MSP",
            BillingEmail = "billing@example.com",
            Type = ProviderType.Msp,
            Status = ProviderStatusType.Billable,
            Enabled = enabled,
        });

        var apiKey = await factory.Services.GetRequiredService<IProviderApiKeyRepository>().CreateAsync(new ProviderApiKey
        {
            ProviderId = provider.Id,
            Type = ProviderApiKeyType.BillingReadOnly,
            ApiKey = "provider-test-api-key-0123456",
            RevisionDate = DateTime.UtcNow,
        });

        return (provider, apiKey);
    }

    private static Task<HttpContext> PostTokenAsync(IdentityApplicationFactory factory, string clientId, string secret) =>
        factory.Server.PostAsync("/connect/token", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            { "grant_type", "client_credentials" },
            { "client_id", clientId },
            { "client_secret", secret },
            { "scope", ApiScopes.ApiProvider },
        }));

    private static async Task AssertInvalidClientAsync(HttpContext context)
    {
        Assert.Equal(StatusCodes.Status400BadRequest, context.Response.StatusCode);
        using var errorBody = await AssertHelper.AssertResponseTypeIs<JsonDocument>(context);
        var error = AssertHelper.AssertJsonProperty(errorBody.RootElement, "error", JsonValueKind.String).GetString();
        Assert.Equal("invalid_client", error);
    }

    private static JsonElement ReadJwtPayload(string token)
    {
        var payload = token.Split('.')[1].Replace('-', '+').Replace('_', '/');
        payload = payload.PadRight(payload.Length + ((4 - payload.Length % 4) % 4), '=');
        return JsonDocument.Parse(Convert.FromBase64String(payload)).RootElement.Clone();
    }

    private static IEnumerable<string?> ReadScopes(JsonElement claims)
    {
        var scope = claims.GetProperty("scope");
        return scope.ValueKind == JsonValueKind.Array
            ? scope.EnumerateArray().Select(s => s.GetString())
            : [scope.GetString()];
    }
}
