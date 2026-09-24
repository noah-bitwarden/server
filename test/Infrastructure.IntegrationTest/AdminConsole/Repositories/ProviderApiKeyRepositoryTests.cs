using Bit.Core.AdminConsole.Entities.Provider;
using Bit.Core.AdminConsole.Enums.Provider;
using Bit.Core.AdminConsole.Repositories;
using Xunit;

namespace Bit.Infrastructure.IntegrationTest.AdminConsole.Repositories;

public class ProviderApiKeyRepositoryTests
{
    [Theory, DatabaseData]
    public async Task CreateAsync_GetManyByProviderIdTypeAsync_ReturnsKeyForProvider(
        IProviderRepository providerRepository,
        IProviderApiKeyRepository providerApiKeyRepository)
    {
        // Arrange
        var provider = await CreateTestProviderAsync(providerRepository);
        var otherProvider = await CreateTestProviderAsync(providerRepository);

        var apiKey = await providerApiKeyRepository.CreateAsync(CreateTestApiKey(provider.Id));
        await providerApiKeyRepository.CreateAsync(CreateTestApiKey(otherProvider.Id));

        // Act
        var byType = (await providerApiKeyRepository
            .GetManyByProviderIdTypeAsync(provider.Id, ProviderApiKeyType.BillingReadOnly)).ToList();
        var anyType = (await providerApiKeyRepository.GetManyByProviderIdTypeAsync(provider.Id)).ToList();

        // Assert
        var result = Assert.Single(byType);
        Assert.Equal(apiKey.Id, result.Id);
        Assert.Equal(provider.Id, result.ProviderId);
        Assert.Equal(ProviderApiKeyType.BillingReadOnly, result.Type);
        Assert.Equal(apiKey.ApiKey, result.ApiKey);
        Assert.Equal(apiKey.RevisionDate, result.RevisionDate, TimeSpan.FromMilliseconds(10));

        Assert.Equal(apiKey.Id, Assert.Single(anyType).Id);
    }

    [Theory, DatabaseData]
    public async Task GetManyByProviderIdTypeAsync_NoKeys_ReturnsEmpty(
        IProviderRepository providerRepository,
        IProviderApiKeyRepository providerApiKeyRepository)
    {
        var provider = await CreateTestProviderAsync(providerRepository);

        var result = await providerApiKeyRepository
            .GetManyByProviderIdTypeAsync(provider.Id, ProviderApiKeyType.BillingReadOnly);

        Assert.Empty(result);
    }

    [Theory, DatabaseData]
    public async Task ReplaceAsync_UpdatesApiKeyAndRevisionDate(
        IProviderRepository providerRepository,
        IProviderApiKeyRepository providerApiKeyRepository)
    {
        // Arrange
        var provider = await CreateTestProviderAsync(providerRepository);
        var apiKey = await providerApiKeyRepository.CreateAsync(CreateTestApiKey(provider.Id));

        var newRevisionDate = apiKey.RevisionDate.AddMinutes(5);
        apiKey.ApiKey = "rotated-api-key-value-00000000";
        apiKey.RevisionDate = newRevisionDate;

        // Act
        await providerApiKeyRepository.ReplaceAsync(apiKey);

        // Assert
        var result = Assert.Single(await providerApiKeyRepository.GetManyByProviderIdTypeAsync(provider.Id));
        Assert.Equal(apiKey.Id, result.Id);
        Assert.Equal("rotated-api-key-value-00000000", result.ApiKey);
        Assert.Equal(newRevisionDate, result.RevisionDate, TimeSpan.FromMilliseconds(10));
    }

    [Theory, DatabaseData]
    public async Task CreateAsync_DuplicateProviderIdAndType_Throws(
        IProviderRepository providerRepository,
        IProviderApiKeyRepository providerApiKeyRepository)
    {
        var provider = await CreateTestProviderAsync(providerRepository);
        await providerApiKeyRepository.CreateAsync(CreateTestApiKey(provider.Id));

        await Assert.ThrowsAnyAsync<Exception>(
            () => providerApiKeyRepository.CreateAsync(CreateTestApiKey(provider.Id)));

        Assert.Single(await providerApiKeyRepository.GetManyByProviderIdTypeAsync(provider.Id));
    }

    [Theory, DatabaseData]
    public async Task DeleteAsync_RemovesKey(
        IProviderRepository providerRepository,
        IProviderApiKeyRepository providerApiKeyRepository)
    {
        var provider = await CreateTestProviderAsync(providerRepository);
        var apiKey = await providerApiKeyRepository.CreateAsync(CreateTestApiKey(provider.Id));

        await providerApiKeyRepository.DeleteAsync(apiKey);

        Assert.Empty(await providerApiKeyRepository.GetManyByProviderIdTypeAsync(provider.Id));
    }

    [Theory, DatabaseData]
    public async Task ProviderDeleteAsync_CascadesToProviderApiKeys(
        IProviderRepository providerRepository,
        IProviderApiKeyRepository providerApiKeyRepository)
    {
        // Arrange
        var provider = await CreateTestProviderAsync(providerRepository);
        var otherProvider = await CreateTestProviderAsync(providerRepository);
        await providerApiKeyRepository.CreateAsync(CreateTestApiKey(provider.Id));
        var otherApiKey = await providerApiKeyRepository.CreateAsync(CreateTestApiKey(otherProvider.Id));

        // Act
        await providerRepository.DeleteAsync(provider);

        // Assert
        Assert.Empty(await providerApiKeyRepository.GetManyByProviderIdTypeAsync(provider.Id));
        var remaining = Assert.Single(await providerApiKeyRepository.GetManyByProviderIdTypeAsync(otherProvider.Id));
        Assert.Equal(otherApiKey.Id, remaining.Id);
    }

    private static Task<Provider> CreateTestProviderAsync(IProviderRepository providerRepository) =>
        providerRepository.CreateAsync(new Provider
        {
            Name = "Test Provider",
            Enabled = true,
            Type = ProviderType.Msp,
            Status = ProviderStatusType.Billable,
        });

    private static ProviderApiKey CreateTestApiKey(Guid providerId) => new()
    {
        ProviderId = providerId,
        Type = ProviderApiKeyType.BillingReadOnly,
        ApiKey = Guid.NewGuid().ToString("N")[..30],
        RevisionDate = DateTime.UtcNow,
    };
}
