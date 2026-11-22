using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GameVault.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCatalogueSortIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Games_RatingAverage_RatingCount",
                table: "Games");

            migrationBuilder.CreateIndex(
                name: "IX_Games_CreatedAt_Title",
                table: "Games",
                columns: new[] { "CreatedAt", "Title" },
                descending: new[] { true, false });

            migrationBuilder.CreateIndex(
                name: "IX_Games_Rating_Title",
                table: "Games",
                columns: new[] { "RatingAverage", "RatingCount", "Title" },
                descending: new[] { true, true, false });

            migrationBuilder.CreateIndex(
                name: "IX_Games_ReleaseDate_Title",
                table: "Games",
                columns: new[] { "ReleaseDate", "Title" },
                descending: new[] { true, false });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Games_CreatedAt_Title",
                table: "Games");

            migrationBuilder.DropIndex(
                name: "IX_Games_Rating_Title",
                table: "Games");

            migrationBuilder.DropIndex(
                name: "IX_Games_ReleaseDate_Title",
                table: "Games");

            migrationBuilder.CreateIndex(
                name: "IX_Games_RatingAverage_RatingCount",
                table: "Games",
                columns: new[] { "RatingAverage", "RatingCount" });
        }
    }
}
