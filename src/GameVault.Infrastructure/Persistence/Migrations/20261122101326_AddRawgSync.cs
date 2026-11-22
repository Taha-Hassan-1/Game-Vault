using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GameVault.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddRawgSync : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "DetailsFetchedAt",
                table: "Games",
                type: "datetime2",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ApiUsage",
                columns: table => new
                {
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    Provider = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    Count = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ApiUsage", x => new { x.Date, x.Provider });
                });

            migrationBuilder.CreateTable(
                name: "SyncStates",
                columns: table => new
                {
                    Key = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Value = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SyncStates", x => x.Key);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ApiUsage");

            migrationBuilder.DropTable(
                name: "SyncStates");

            migrationBuilder.DropColumn(
                name: "DetailsFetchedAt",
                table: "Games");
        }
    }
}
