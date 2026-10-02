using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Bit.SqliteMigrations.Migrations
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
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "AutoscaleSeatLimit",
                table: "ProviderOrganization",
                type: "INTEGER",
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
