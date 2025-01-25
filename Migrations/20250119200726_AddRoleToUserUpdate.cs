using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MovieRecommendationAPI.Migrations
{
    /// <inheritdoc />
    public partial class AddRoleToUserUpdate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsTopRated",
                table: "movies",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsTopRated",
                table: "movies");
        }
    }
}
