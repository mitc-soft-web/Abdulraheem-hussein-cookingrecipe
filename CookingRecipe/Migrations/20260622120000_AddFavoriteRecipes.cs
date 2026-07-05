using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CookingRecipe.Migrations
{
    /// <inheritdoc />
    public partial class AddFavoriteRecipes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "FavoriteRecipes",
                columns: table => new
                {
                    DeviceId = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    RecipeId = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FavoriteRecipes", x => new { x.DeviceId, x.RecipeId });
                });

            migrationBuilder.CreateIndex(
                name: "IX_FavoriteRecipes_DeviceId",
                table: "FavoriteRecipes",
                column: "DeviceId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "FavoriteRecipes");
        }
    }
}
