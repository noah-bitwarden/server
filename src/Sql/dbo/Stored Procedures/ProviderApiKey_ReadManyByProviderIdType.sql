CREATE PROCEDURE [dbo].[ProviderApiKey_ReadManyByProviderIdType]
    @ProviderId UNIQUEIDENTIFIER,
    @Type TINYINT = NULL
AS
BEGIN
    SET NOCOUNT ON

    SELECT
        *
    FROM
        [dbo].[ProviderApiKeyView]
    WHERE
        [ProviderId] = @ProviderId AND
        (@Type IS NULL OR [Type] = @Type)
END
