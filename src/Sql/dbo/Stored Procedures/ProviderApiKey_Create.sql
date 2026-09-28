CREATE PROCEDURE [dbo].[ProviderApiKey_Create]
    @Id UNIQUEIDENTIFIER OUTPUT,
    @ProviderId UNIQUEIDENTIFIER,
    @Type TINYINT,
    @ApiKey VARCHAR(30),
    @RevisionDate DATETIME2(7)
AS
BEGIN
    SET NOCOUNT ON

    INSERT INTO [dbo].[ProviderApiKey]
    (
        [Id],
        [ProviderId],
        [Type],
        [ApiKey],
        [RevisionDate]
    )
    VALUES
    (
        @Id,
        @ProviderId,
        @Type,
        @ApiKey,
        @RevisionDate
    )
END
