CREATE PROCEDURE [dbo].[ProviderApiKey_DeleteById]
    @Id UNIQUEIDENTIFIER
AS
BEGIN
    SET NOCOUNT ON

    DELETE
    FROM
        [dbo].[ProviderApiKey]
    WHERE
        [Id] = @Id
END
