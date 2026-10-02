CREATE PROCEDURE [dbo].[ProviderOrganization_TryAutoscaleSeats]
    @OrganizationId UNIQUEIDENTIFIER,
    @PlanName NVARCHAR(50),
    @SeatsToAdd INT,
    @RevisionDate DATETIME2(7),
    @ValidateOnly BIT = 0
AS
BEGIN
    SET NOCOUNT ON
    SET XACT_ABORT ON

    -- Result codes map to Bit.Core.AdminConsole.Models.Data.Provider.ProviderOrganizationAutoscaleSeatsResult:
    -- 0 Success, 1 NotEnabled, 2 NoPool, 3 PoolExhausted, 4 ClientLimitReached, 5 ClientNotManaged
    DECLARE @Result INT
    DECLARE @ProviderId UNIQUEIDENTIFIER
    DECLARE @PlanType TINYINT
    DECLARE @AutoscaleEnabled BIT
    DECLARE @AutoscaleSeatLimit INT
    DECLARE @ClientSeats INT
    DECLARE @ProviderPlanId UNIQUEIDENTIFIER
    DECLARE @SeatMinimum INT
    DECLARE @PurchasedSeats INT
    DECLARE @AllocatedSeats INT
    DECLARE @AssignedSeats INT

    BEGIN TRANSACTION

    SELECT
        @ProviderId = PO.[ProviderId],
        @PlanType = O.[PlanType],
        @AutoscaleEnabled = PO.[AutoscaleEnabled],
        @AutoscaleSeatLimit = PO.[AutoscaleSeatLimit]
    FROM
        [dbo].[ProviderOrganization] PO
    INNER JOIN
        [dbo].[Organization] O ON O.[Id] = PO.[OrganizationId]
    WHERE
        PO.[OrganizationId] = @OrganizationId
        AND O.[Status] = 2 -- Managed
        AND O.[Seats] IS NOT NULL

    IF @ProviderId IS NULL
    BEGIN
        SET @Result = 5
    END
    ELSE IF @AutoscaleEnabled = 0
    BEGIN
        SET @Result = 1
    END
    ELSE
    BEGIN
        -- Locking the provider plan row serializes every autoscale that draws on the same seat minimum
        SELECT
            @ProviderPlanId = [Id],
            @SeatMinimum = [SeatMinimum],
            @PurchasedSeats = [PurchasedSeats],
            @AllocatedSeats = [AllocatedSeats]
        FROM
            [dbo].[ProviderPlan] WITH (UPDLOCK, HOLDLOCK)
        WHERE
            [ProviderId] = @ProviderId
            AND [PlanType] = @PlanType

        IF @ProviderPlanId IS NULL OR @SeatMinimum IS NULL OR @PurchasedSeats IS NULL OR @AllocatedSeats IS NULL
        BEGIN
            SET @Result = 2
        END
        ELSE
        BEGIN
            -- Read under the plan lock so a concurrent autoscale for this client is already reflected
            SELECT
                @ClientSeats = [Seats]
            FROM
                [dbo].[Organization]
            WHERE
                [Id] = @OrganizationId

            -- Mirrors ProviderBillingService.GetAssignedSeatTotalAsync: managed clients on the same plan name
            SELECT
                @AssignedSeats = ISNULL(SUM(O.[Seats]), 0)
            FROM
                [dbo].[ProviderOrganization] PO
            INNER JOIN
                [dbo].[Organization] O ON O.[Id] = PO.[OrganizationId]
            WHERE
                PO.[ProviderId] = @ProviderId
                AND O.[Status] = 2 -- Managed
                AND O.[Plan] = @PlanName

            IF @AssignedSeats + @SeatsToAdd > @SeatMinimum
            BEGIN
                SET @Result = 3
            END
            ELSE IF @AutoscaleSeatLimit IS NOT NULL AND @ClientSeats + @SeatsToAdd > @AutoscaleSeatLimit
            BEGIN
                SET @Result = 4
            END
            ELSE
            BEGIN
                IF @ValidateOnly = 0
                BEGIN
                    UPDATE
                        [dbo].[Organization]
                    SET
                        [Seats] = [Seats] + @SeatsToAdd,
                        [RevisionDate] = @RevisionDate
                    WHERE
                        [Id] = @OrganizationId

                    -- Matches ProviderBillingService.ScaleSeats when the total stays at or below the seat minimum
                    UPDATE
                        [dbo].[ProviderPlan]
                    SET
                        [AllocatedSeats] = @AssignedSeats + @SeatsToAdd
                    WHERE
                        [Id] = @ProviderPlanId
                END

                SET @Result = 0
            END
        END
    END

    COMMIT TRANSACTION

    SELECT @Result
END
