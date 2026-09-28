CREATE PROCEDURE [dbo].[ProviderApiKey_Update]
    @Id UNIQUEIDENTIFIER,
    @ProviderId UNIQUEIDENTIFIER,
    @Type TINYINT,
    @ApiKey VARCHAR(30),
    @RevisionDate DATETIME2(7)
AS
BEGIN
    SET NOCOUNT ON

    UPDATE
        [dbo].[ProviderApiKey]
    SET
        [ApiKey] = @ApiKey,
        [RevisionDate] = @RevisionDate
    WHERE
        [Id] = @Id
END
