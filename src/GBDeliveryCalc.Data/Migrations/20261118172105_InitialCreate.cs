using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GBDeliveryCalc.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CompanySettings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false),
                    OriginAddress = table.Column<string>(type: "TEXT", nullable: false),
                    DefaultInstallMinutes = table.Column<int>(type: "INTEGER", nullable: false),
                    AllInMileageRatePerMile = table.Column<decimal>(type: "TEXT", nullable: false),
                    VehicleRatePlusFuelPerMile = table.Column<decimal>(type: "TEXT", nullable: true),
                    DefaultBillingMode = table.Column<int>(type: "INTEGER", nullable: false),
                    IntegrationBackendUrl = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CompanySettings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Estimates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    QuoteNumber = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    CustomerName = table.Column<string>(type: "TEXT", nullable: false),
                    Destination = table.Column<string>(type: "TEXT", nullable: false),
                    TeamName = table.Column<string>(type: "TEXT", nullable: false),
                    QuoteTotal = table.Column<decimal>(type: "TEXT", nullable: true),
                    SnapshotSchemaVersion = table.Column<int>(type: "INTEGER", nullable: false),
                    SnapshotJson = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Estimates", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "FuelPriceCache",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    FuelType = table.Column<int>(type: "INTEGER", nullable: false),
                    PricePerGallon = table.Column<decimal>(type: "TEXT", nullable: false),
                    Source = table.Column<string>(type: "TEXT", nullable: false),
                    PublishedDate = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    RetrievedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    IsLive = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FuelPriceCache", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Teams",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Description = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    BillingRatePerHour = table.Column<decimal>(type: "TEXT", nullable: false),
                    LoadedLaborCostPerHour = table.Column<decimal>(type: "TEXT", nullable: true),
                    DefaultVehicleId = table.Column<Guid>(type: "TEXT", nullable: true),
                    IsArchived = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Teams", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Vehicles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    FuelType = table.Column<int>(type: "INTEGER", nullable: false),
                    EstimatedMpg = table.Column<decimal>(type: "TEXT", nullable: true),
                    OperatingCostPerMileExcludingFuel = table.Column<decimal>(type: "TEXT", nullable: true),
                    IsArchived = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Vehicles", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "EstimateActuals",
                columns: table => new
                {
                    EstimateId = table.Column<Guid>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    ActualsJson = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EstimateActuals", x => x.EstimateId);
                    table.ForeignKey(
                        name: "FK_EstimateActuals_Estimates_EstimateId",
                        column: x => x.EstimateId,
                        principalTable: "Estimates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.InsertData(
                table: "CompanySettings",
                columns: new[] { "Id", "AllInMileageRatePerMile", "DefaultBillingMode", "DefaultInstallMinutes", "IntegrationBackendUrl", "OriginAddress", "VehicleRatePlusFuelPerMile" },
                values: new object[] { 1, 0.725m, 0, 180, null, "", null });

            migrationBuilder.CreateIndex(
                name: "IX_Estimates_QuoteNumber",
                table: "Estimates",
                column: "QuoteNumber",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FuelPriceCache_FuelType",
                table: "FuelPriceCache",
                column: "FuelType");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CompanySettings");

            migrationBuilder.DropTable(
                name: "EstimateActuals");

            migrationBuilder.DropTable(
                name: "FuelPriceCache");

            migrationBuilder.DropTable(
                name: "Teams");

            migrationBuilder.DropTable(
                name: "Vehicles");

            migrationBuilder.DropTable(
                name: "Estimates");
        }
    }
}
