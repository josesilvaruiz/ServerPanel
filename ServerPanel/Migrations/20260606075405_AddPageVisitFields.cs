using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ServerPanel.Migrations
{
    /// <inheritdoc />
    public partial class AddPageVisitFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Language",
                table: "PageVisits",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ScreenHeight",
                table: "PageVisits",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ScreenWidth",
                table: "PageVisits",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "TimeOnPageSeconds",
                table: "PageVisits",
                type: "integer",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Language",
                table: "PageVisits");

            migrationBuilder.DropColumn(
                name: "ScreenHeight",
                table: "PageVisits");

            migrationBuilder.DropColumn(
                name: "ScreenWidth",
                table: "PageVisits");

            migrationBuilder.DropColumn(
                name: "TimeOnPageSeconds",
                table: "PageVisits");
        }
    }
}
