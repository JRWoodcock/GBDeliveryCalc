using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GBDeliveryCalc.Data.Migrations
{
    /// <inheritdoc />
    public partial class FuelPricesByState : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_FuelPriceCache_FuelType",
                table: "FuelPriceCache");

            migrationBuilder.DropPrimaryKey(
                name: "PK_FuelFetchStates",
                table: "FuelFetchStates");

            migrationBuilder.AddColumn<string>(
                name: "State",
                table: "FuelPriceCache",
                type: "TEXT",
                maxLength: 2,
                nullable: false,
                defaultValue: "MS");

            migrationBuilder.AddColumn<string>(
                name: "State",
                table: "FuelFetchStates",
                type: "TEXT",
                maxLength: 2,
                nullable: false,
                defaultValue: "MS");

            migrationBuilder.AddPrimaryKey(
                name: "PK_FuelFetchStates",
                table: "FuelFetchStates",
                columns: new[] { "FuelType", "State" });

            migrationBuilder.CreateIndex(
                name: "IX_FuelPriceCache_FuelType_State",
                table: "FuelPriceCache",
                columns: new[] { "FuelType", "State" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_FuelPriceCache_FuelType_State",
                table: "FuelPriceCache");

            migrationBuilder.DropPrimaryKey(
                name: "PK_FuelFetchStates",
                table: "FuelFetchStates");

            migrationBuilder.DropColumn(
                name: "State",
                table: "FuelPriceCache");

            migrationBuilder.DropColumn(
                name: "State",
                table: "FuelFetchStates");

            migrationBuilder.AddPrimaryKey(
                name: "PK_FuelFetchStates",
                table: "FuelFetchStates",
                column: "FuelType");

            migrationBuilder.CreateIndex(
                name: "IX_FuelPriceCache_FuelType",
                table: "FuelPriceCache",
                column: "FuelType");
        }
    }
}
