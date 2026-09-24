-- ProviderApiKey

-- Table
IF OBJECT_ID('[dbo].[ProviderApiKey]') IS NULL
BEGIN
    CREATE TABLE [dbo].[ProviderApiKey] (
        [Id]           UNIQUEIDENTIFIER NOT NULL,
        [ProviderId]   UNIQUEIDENTIFIER NOT NULL,
        [Type]         TINYINT          NOT NULL,
        [ApiKey]       VARCHAR(30)      NOT NULL,
        [RevisionDate] DATETIME2(7)     NOT NULL,
        CONSTRAINT [PK_ProviderApiKey] PRIMARY KEY CLUSTERED ([Id] ASC),
        CONSTRAINT [FK_ProviderApiKey_Provider] FOREIGN KEY ([ProviderId]) REFERENCES [dbo].[Provider] ([Id]) ON DELETE CASCADE
    );
END
GO

-- Index
IF NOT EXISTS (
    SELECT
        NULL
    FROM
        sys.indexes
    WHERE
        [name] = 'IX_ProviderApiKey_ProviderId_Type'
        AND object_id = OBJECT_ID('[dbo].[ProviderApiKey]')
)
BEGIN
    CREATE UNIQUE NONCLUSTERED INDEX [IX_ProviderApiKey_ProviderId_Type]
        ON [dbo].[ProviderApiKey]([ProviderId] ASC, [Type] ASC);
END
GO

-- View
CREATE OR ALTER VIEW [dbo].[ProviderApiKeyView]
AS
SELECT
    *
FROM
    [dbo].[ProviderApiKey]
GO

-- Stored Procedures: Create
CREATE OR ALTER PROCEDURE [dbo].[ProviderApiKey_Create]
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
GO

-- Stored Procedures: Update
CREATE OR ALTER PROCEDURE [dbo].[ProviderApiKey_Update]
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
GO

-- Stored Procedures: ReadManyByProviderIdType
CREATE OR ALTER PROCEDURE [dbo].[ProviderApiKey_ReadManyByProviderIdType]
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
GO

-- Stored Procedures: DeleteById
CREATE OR ALTER PROCEDURE [dbo].[ProviderApiKey_DeleteById]
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
GO
