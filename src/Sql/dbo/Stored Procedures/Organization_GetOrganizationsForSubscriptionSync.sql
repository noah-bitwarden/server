CREATE PROCEDURE [dbo].[Organization_GetOrganizationsForSubscriptionSync]
AS
BEGIN
    SELECT *
    FROM [dbo].[OrganizationView]
    WHERE [Seats] IS NOT NULL AND [SyncSeats] = 1
        -- Managed clients are billed through their provider's subscription, never their own
        AND [Status] <> 2
END
