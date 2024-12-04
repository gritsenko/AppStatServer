using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AppStatServer.Migrations
{
    /// <inheritdoc />
    public partial class EventStackTrace : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "StackTrace",
                table: "Events",
                type: "longtext",
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "StackTrace",
                table: "Events");
        }
    }
}
