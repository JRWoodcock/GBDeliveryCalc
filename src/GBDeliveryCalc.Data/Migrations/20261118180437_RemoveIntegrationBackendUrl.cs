using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GBDeliveryCalc.Data.Migrations
{
    /// <inheritdoc />
    public partial class RemoveIntegrationBackendUrl : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IntegrationBackendUrl",
                table: "CompanySettings");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "IntegrationBackendUrl",
                table: "CompanySettings",
                type: "TEXT",
                nullable: true);

            migrationBuilder.UpdateData(
                table: "CompanySettings",
                keyColumn: "Id",
                keyValue: 1,
                column: "IntegrationBackendUrl",
                value: null);
        }
    }
}
