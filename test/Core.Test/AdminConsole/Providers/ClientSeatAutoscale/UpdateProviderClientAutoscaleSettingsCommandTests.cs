using Bit.Core.AdminConsole.Entities;
using Bit.Core.AdminConsole.Entities.Provider;
using Bit.Core.AdminConsole.Enums.Provider;
using Bit.Core.AdminConsole.Providers.ClientSeatAutoscale;
using Bit.Core.AdminConsole.Repositories;
using Bit.Core.Enums;
using Bit.Core.Repositories;
using Bit.Core.Services;
using Bit.Test.Common.AutoFixture;
using Bit.Test.Common.AutoFixture.Attributes;
using NSubstitute;
using Xunit;

namespace Bit.Core.Test.AdminConsole.Providers.ClientSeatAutoscale;

[SutProviderCustomize]
public class UpdateProviderClientAutoscaleSettingsCommandTests
{
    [Theory]
    [BitAutoData((object?)null)]
    [BitAutoData(10)]
    [BitAutoData(25)]
    public async Task UpdateAsync_ValidRequest_SavesSettingsAndLogsEvent(int? seatLimit,
        SutProvider<UpdateProviderClientAutoscaleSettingsCommand> sutProvider, Provider provider,
        Organization organization)
    {
        var providerOrganization = Arrange(sutProvider, provider, organization);

        var result = await sutProvider.Sut.UpdateAsync(
            new UpdateProviderClientAutoscaleSettingsRequest(provider.Id, organization.Id, true, seatLimit));

        Assert.True(result.IsSuccess);
        Assert.Same(providerOrganization, result.AsSuccess);
        Assert.True(providerOrganization.AutoscaleEnabled);
        Assert.Equal(seatLimit, providerOrganization.AutoscaleSeatLimit);
        await sutProvider.GetDependency<IProviderOrganizationRepository>().Received(1)
            .ReplaceAsync(providerOrganization);
        await sutProvider.GetDependency<IEventService>().Received(1)
            .LogProviderOrganizationEventAsync(providerOrganization, EventType.ProviderOrganization_AutoscaleUpdated);
    }

    [Theory, BitAutoData]
    public async Task UpdateAsync_Disable_ClearsEnabled(SutProvider<UpdateProviderClientAutoscaleSettingsCommand> sutProvider,
        Provider provider, Organization organization)
    {
        var providerOrganization = Arrange(sutProvider, provider, organization);
        providerOrganization.AutoscaleEnabled = true;

        var result = await sutProvider.Sut.UpdateAsync(
            new UpdateProviderClientAutoscaleSettingsRequest(provider.Id, organization.Id, false, 15));

        Assert.True(result.IsSuccess);
        Assert.False(providerOrganization.AutoscaleEnabled);
        Assert.Equal(15, providerOrganization.AutoscaleSeatLimit);
    }

    [Theory, BitAutoData]
    public async Task UpdateAsync_ProviderNotFound_ReturnsNotFound(
        SutProvider<UpdateProviderClientAutoscaleSettingsCommand> sutProvider, Provider provider,
        Organization organization)
    {
        Arrange(sutProvider, provider, organization);
        sutProvider.GetDependency<IProviderRepository>().GetByIdAsync(provider.Id).Returns((Provider?)null);

        var result = await sutProvider.Sut.UpdateAsync(
            new UpdateProviderClientAutoscaleSettingsRequest(provider.Id, organization.Id, true, null));

        Assert.IsType<ProviderNotFound>(result.AsError);
        await AssertNotSavedAsync(sutProvider);
    }

    [Theory]
    [BitAutoData(ProviderType.Reseller, ProviderStatusType.Billable, true)]
    [BitAutoData(ProviderType.BusinessUnit, ProviderStatusType.Billable, true)]
    [BitAutoData(ProviderType.Msp, ProviderStatusType.Created, true)]
    [BitAutoData(ProviderType.Msp, ProviderStatusType.Billable, false)]
    public async Task UpdateAsync_ProviderNotEligible_ReturnsBadRequest(ProviderType type, ProviderStatusType status,
        bool enabled, SutProvider<UpdateProviderClientAutoscaleSettingsCommand> sutProvider, Provider provider,
        Organization organization)
    {
        Arrange(sutProvider, provider, organization);
        provider.Type = type;
        provider.Status = status;
        provider.Enabled = enabled;

        var result = await sutProvider.Sut.UpdateAsync(
            new UpdateProviderClientAutoscaleSettingsRequest(provider.Id, organization.Id, true, null));

        Assert.IsType<ProviderNotEligibleForAutoscale>(result.AsError);
        await AssertNotSavedAsync(sutProvider);
    }

    [Theory, BitAutoData]
    public async Task UpdateAsync_ClientNotLinkedToProvider_ReturnsNotFound(
        SutProvider<UpdateProviderClientAutoscaleSettingsCommand> sutProvider, Provider provider,
        Organization organization)
    {
        Arrange(sutProvider, provider, organization);
        sutProvider.GetDependency<IProviderOrganizationRepository>().GetByOrganizationId(organization.Id)
            .Returns((ProviderOrganization?)null);

        var result = await sutProvider.Sut.UpdateAsync(
            new UpdateProviderClientAutoscaleSettingsRequest(provider.Id, organization.Id, true, null));

        Assert.IsType<ClientNotFound>(result.AsError);
        await AssertNotSavedAsync(sutProvider);
    }

    [Theory, BitAutoData]
    public async Task UpdateAsync_ClientBelongsToAnotherProvider_ReturnsNotFound(
        SutProvider<UpdateProviderClientAutoscaleSettingsCommand> sutProvider, Provider provider,
        Organization organization, Guid otherProviderId)
    {
        var providerOrganization = Arrange(sutProvider, provider, organization);
        providerOrganization.ProviderId = otherProviderId;

        var result = await sutProvider.Sut.UpdateAsync(
            new UpdateProviderClientAutoscaleSettingsRequest(provider.Id, organization.Id, true, null));

        Assert.IsType<ClientNotFound>(result.AsError);
        await AssertNotSavedAsync(sutProvider);
    }

    [Theory]
    [BitAutoData(OrganizationStatusType.Created)]
    [BitAutoData(OrganizationStatusType.Pending)]
    public async Task UpdateAsync_ClientNotManaged_ReturnsBadRequest(OrganizationStatusType status,
        SutProvider<UpdateProviderClientAutoscaleSettingsCommand> sutProvider, Provider provider,
        Organization organization)
    {
        Arrange(sutProvider, provider, organization);
        organization.Status = status;

        var result = await sutProvider.Sut.UpdateAsync(
            new UpdateProviderClientAutoscaleSettingsRequest(provider.Id, organization.Id, true, null));

        Assert.IsType<ClientNotManagedByProvider>(result.AsError);
        await AssertNotSavedAsync(sutProvider);
    }

    [Theory]
    [BitAutoData(0)]
    [BitAutoData(-5)]
    [BitAutoData(9)]
    public async Task UpdateAsync_InvalidSeatLimit_ReturnsBadRequest(int seatLimit,
        SutProvider<UpdateProviderClientAutoscaleSettingsCommand> sutProvider, Provider provider,
        Organization organization)
    {
        Arrange(sutProvider, provider, organization);

        var result = await sutProvider.Sut.UpdateAsync(
            new UpdateProviderClientAutoscaleSettingsRequest(provider.Id, organization.Id, true, seatLimit));

        Assert.IsType<InvalidAutoscaleSeatLimit>(result.AsError);
        await AssertNotSavedAsync(sutProvider);
    }

    private static ProviderOrganization Arrange(SutProvider<UpdateProviderClientAutoscaleSettingsCommand> sutProvider,
        Provider provider, Organization organization)
    {
        provider.Type = ProviderType.Msp;
        provider.Status = ProviderStatusType.Billable;
        provider.Enabled = true;
        sutProvider.GetDependency<IProviderRepository>().GetByIdAsync(provider.Id).Returns(provider);

        organization.Status = OrganizationStatusType.Managed;
        organization.Seats = 10;
        sutProvider.GetDependency<IOrganizationRepository>().GetByIdAsync(organization.Id).Returns(organization);

        var providerOrganization = new ProviderOrganization
        {
            Id = Guid.NewGuid(),
            ProviderId = provider.Id,
            OrganizationId = organization.Id
        };
        sutProvider.GetDependency<IProviderOrganizationRepository>().GetByOrganizationId(organization.Id)
            .Returns(providerOrganization);

        return providerOrganization;
    }

    private static async Task AssertNotSavedAsync(SutProvider<UpdateProviderClientAutoscaleSettingsCommand> sutProvider)
    {
        await sutProvider.GetDependency<IProviderOrganizationRepository>().DidNotReceiveWithAnyArgs()
            .ReplaceAsync(default!);
        await sutProvider.GetDependency<IEventService>().DidNotReceiveWithAnyArgs()
            .LogProviderOrganizationEventAsync(default!, default);
    }
}
