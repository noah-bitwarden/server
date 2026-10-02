using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Bit.MySqlMigrations.Migrations
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
                type: "tinyint(1)",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "AutoscaleSeatLimit",
                table: "ProviderOrganization",
                type: "int",
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
