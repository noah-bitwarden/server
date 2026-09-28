using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Bit.SqliteMigrations.Migrations;

/// <inheritdoc />
public partial class AddProviderApiKey : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "ProviderApiKey",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "TEXT", nullable: false),
                ProviderId = table.Column<Guid>(type: "TEXT", nullable: false),
                Type = table.Column<byte>(type: "INTEGER", nullable: false),
                ApiKey = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                RevisionDate = table.Column<DateTime>(type: "TEXT", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_ProviderApiKey", x => x.Id);
                table.ForeignKey(
                    name: "FK_ProviderApiKey_Provider_ProviderId",
                    column: x => x.ProviderId,
                    principalTable: "Provider",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_ProviderApiKey_ProviderId_Type",
            table: "ProviderApiKey",
            columns: new[] { "ProviderId", "Type" },
            unique: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "ProviderApiKey");
    }
}
