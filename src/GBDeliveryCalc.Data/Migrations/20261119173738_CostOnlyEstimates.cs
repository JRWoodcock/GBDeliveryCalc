using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GBDeliveryCalc.Data.Migrations
{
    /// <inheritdoc />
    public partial class CostOnlyEstimates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Cost-only estimates (2026-11-19): customer billing is removed.
            migrationBuilder.AddColumn<decimal>(name: "TotalCost", table: "Estimates", type: "TEXT", nullable: true);
            migrationBuilder.AddColumn<DateTimeOffset>(name: "UpdatedAt", table: "Estimates", type: "TEXT", nullable: true);
            // Billing-era rows listed their quote (revenue) total; list their stored cost total instead.
            migrationBuilder.Sql("UPDATE Estimates SET TotalCost = CAST(json_extract(SnapshotJson, '$.Result.TotalDirectCost') AS TEXT) WHERE json_extract(SnapshotJson, '$.Result.TotalDirectCost') IS NOT NULL;");
            migrationBuilder.DropColumn(name: "QuoteTotal", table: "Estimates");

            migrationBuilder.DropColumn(name: "BillingRatePerHour", table: "Teams");
            migrationBuilder.DropColumn(name: "AllInMileageRatePerMile", table: "CompanySettings");
            migrationBuilder.DropColumn(name: "VehicleRatePlusFuelPerMile", table: "CompanySettings");
            migrationBuilder.DropColumn(name: "DefaultBillingMode", table: "CompanySettings");
            migrationBuilder.AddColumn<bool>(name: "RoundLaborHoursUp", table: "CompanySettings", type: "INTEGER", nullable: false, defaultValue: false);

            migrationBuilder.CreateTable(
                name: "EstimateRevisions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    EstimateId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Revision = table.Column<int>(type: "INTEGER", nullable: false),
                    ReplacedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    SnapshotJson = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EstimateRevisions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EstimateRevisions_Estimates_EstimateId",
                        column: x => x.EstimateId,
                        principalTable: "Estimates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_EstimateRevisions_EstimateId_Revision",
                table: "EstimateRevisions",
                columns: new[] { "EstimateId", "Revision" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "EstimateRevisions");
            migrationBuilder.DropColumn(name: "RoundLaborHoursUp", table: "CompanySettings");
            migrationBuilder.AddColumn<int>(name: "DefaultBillingMode", table: "CompanySettings", type: "INTEGER", nullable: false, defaultValue: 0);
            migrationBuilder.AddColumn<decimal>(name: "VehicleRatePlusFuelPerMile", table: "CompanySettings", type: "TEXT", nullable: true);
            migrationBuilder.AddColumn<decimal>(name: "AllInMileageRatePerMile", table: "CompanySettings", type: "TEXT", nullable: false, defaultValue: 0.725m);
            migrationBuilder.AddColumn<decimal>(name: "BillingRatePerHour", table: "Teams", type: "TEXT", nullable: false, defaultValue: 0m);
            migrationBuilder.AddColumn<decimal>(name: "QuoteTotal", table: "Estimates", type: "TEXT", nullable: true);
            migrationBuilder.DropColumn(name: "UpdatedAt", table: "Estimates");
            migrationBuilder.DropColumn(name: "TotalCost", table: "Estimates");
        }
    }
}
