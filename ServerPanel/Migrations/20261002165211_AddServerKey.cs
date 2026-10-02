using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ServerPanel.Migrations
{
    /// <inheritdoc />
    public partial class AddServerKey : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ServerKey",
                table: "Cs2ServerSnapshots",
                type: "text",
                nullable: false,
                defaultValue: "Producción");

            migrationBuilder.AddColumn<string>(
                name: "ServerKey",
                table: "Cs2PlayerSessions",
                type: "text",
                nullable: false,
                defaultValue: "Producción");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ServerKey",
                table: "Cs2ServerSnapshots");

            migrationBuilder.DropColumn(
                name: "ServerKey",
                table: "Cs2PlayerSessions");
        }
    }
}
