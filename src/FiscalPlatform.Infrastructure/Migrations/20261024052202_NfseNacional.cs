using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FiscalPlatform.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class NfseNacional : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "NfseFederalTaxPercent",
                table: "Companies",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "NfseMunicipalTaxPercent",
                table: "Companies",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "NfseSimplesTaxPercent",
                table: "Companies",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "NfseSpecialRegime",
                table: "Companies",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "NfseFederalTaxPercent",
                table: "Companies");

            migrationBuilder.DropColumn(
                name: "NfseMunicipalTaxPercent",
                table: "Companies");

            migrationBuilder.DropColumn(
                name: "NfseSimplesTaxPercent",
                table: "Companies");

            migrationBuilder.DropColumn(
                name: "NfseSpecialRegime",
                table: "Companies");
        }
    }
}
