using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Bit.PostgresMigrations.Migrations
{
    /// <inheritdoc />
    public partial class AddProviderOrganizationAutoscale : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "AutoscaleEnabled",
                table: "ProviderOrganization",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "AutoscaleSeatLimit",
                table: "ProviderOrganization",
                type: "integer",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AutoscaleEnabled",
                table: "ProviderOrganization");

            migrationBuilder.DropColumn(
                name: "AutoscaleSeatLimit",
                table: "ProviderOrganization");
        }
    }
}
