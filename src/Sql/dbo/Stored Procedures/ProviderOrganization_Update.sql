CREATE PROCEDURE [dbo].[ProviderOrganization_Update]
    @Id UNIQUEIDENTIFIER,
    @ProviderId UNIQUEIDENTIFIER,
    @OrganizationId UNIQUEIDENTIFIER,
    @Key VARCHAR(MAX),
    @Settings NVARCHAR(MAX),
    @CreationDate DATETIME2(7),
    @RevisionDate DATETIME2(7),
    @AutoscaleEnabled BIT = 0,
    @AutoscaleSeatLimit INT = NULL
AS
BEGIN
    SET NOCOUNT ON

    UPDATE
        [dbo].[ProviderOrganization]
    SET
        [ProviderId] = @ProviderId,
        [OrganizationId] = @OrganizationId,
        [Key] = @Key,
        [Settings] = @Settings,
        [CreationDate] = @CreationDate,
        [RevisionDate] = @RevisionDate,
        [AutoscaleEnabled] = @AutoscaleEnabled,
        [AutoscaleSeatLimit] = @AutoscaleSeatLimit
    WHERE
        [Id] = @Id
END
