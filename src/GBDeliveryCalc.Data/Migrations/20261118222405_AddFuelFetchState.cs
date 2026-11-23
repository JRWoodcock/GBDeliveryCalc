using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GBDeliveryCalc.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddFuelFetchState : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "FuelFetchStates",
                columns: table => new
                {
                    FuelType = table.Column<int>(type: "INTEGER", nullable: false),
                    LastAttemptAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    LastError = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FuelFetchStates", x => x.FuelType);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "FuelFetchStates");
        }
    }
}
