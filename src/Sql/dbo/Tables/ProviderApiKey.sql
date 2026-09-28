CREATE TABLE [dbo].[ProviderApiKey] (
    [Id]           UNIQUEIDENTIFIER NOT NULL,
    [ProviderId]   UNIQUEIDENTIFIER NOT NULL,
    [Type]         TINYINT          NOT NULL,
    [ApiKey]       VARCHAR(30)      NOT NULL,
    [RevisionDate] DATETIME2(7)     NOT NULL,
    CONSTRAINT [PK_ProviderApiKey] PRIMARY KEY CLUSTERED ([Id] ASC),
    CONSTRAINT [FK_ProviderApiKey_Provider] FOREIGN KEY ([ProviderId]) REFERENCES [dbo].[Provider] ([Id]) ON DELETE CASCADE
);

GO
CREATE UNIQUE NONCLUSTERED INDEX [IX_ProviderApiKey_ProviderId_Type]
    ON [dbo].[ProviderApiKey]([ProviderId] ASC, [Type] ASC);
