using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FiscalPlatform.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Simplificacao : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TaxRegimeHistory");

            migrationBuilder.DropColumn(
                name: "CofinsCst",
                table: "Products");

            migrationBuilder.DropColumn(
                name: "PisCst",
                table: "Products");

            migrationBuilder.DropColumn(
                name: "Nfe_FreightMode",
                table: "Documents");

            migrationBuilder.DropColumn(
                name: "Nfe_Insurance",
                table: "Documents");

            migrationBuilder.DropColumn(
                name: "Nfe_OperationType",
                table: "Documents");

            migrationBuilder.DropColumn(
                name: "Nfe_OtherExpenses",
                table: "Documents");

            migrationBuilder.DropColumn(
                name: "Nfe_Purpose",
                table: "Documents");

            migrationBuilder.DropColumn(
                name: "CofinsCst",
                table: "DocumentItems");

            migrationBuilder.DropColumn(
                name: "PisCst",
                table: "DocumentItems");

            migrationBuilder.DropColumn(
                name: "Cnaes",
                table: "Companies");

            migrationBuilder.DropColumn(
                name: "Crt",
                table: "Companies");

            migrationBuilder.DropColumn(
                name: "LegalNature",
                table: "Companies");

            migrationBuilder.DropColumn(
                name: "NfeCredentialingConfirmed",
                table: "Companies");

            migrationBuilder.DropColumn(
                name: "NfseAccessConfirmed",
                table: "Companies");

            migrationBuilder.DropColumn(
                name: "NfseMunicipalityIbge",
                table: "Companies");

            migrationBuilder.DropColumn(
                name: "TaxRegimeValidFrom",
                table: "Companies");

            migrationBuilder.DropColumn(
                name: "SerialNumber",
                table: "Certificates");

            migrationBuilder.RenameColumn(
                name: "UserName",
                table: "Users",
                newName: "Email");

            migrationBuilder.RenameIndex(
                name: "IX_Users_UserName",
                table: "Users",
                newName: "IX_Users_Email");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "Email",
                table: "Users",
                newName: "UserName");

            migrationBuilder.RenameIndex(
                name: "IX_Users_Email",
                table: "Users",
                newName: "IX_Users_UserName");

            migrationBuilder.AddColumn<string>(
                name: "CofinsCst",
                table: "Products",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PisCst",
                table: "Products",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Nfe_FreightMode",
                table: "Documents",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<decimal>(
                name: "Nfe_Insurance",
                table: "Documents",
                type: "TEXT",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<int>(
                name: "Nfe_OperationType",
                table: "Documents",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<decimal>(
                name: "Nfe_OtherExpenses",
                table: "Documents",
                type: "TEXT",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<int>(
                name: "Nfe_Purpose",
                table: "Documents",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "CofinsCst",
                table: "DocumentItems",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PisCst",
                table: "DocumentItems",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Cnaes",
                table: "Companies",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Crt",
                table: "Companies",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LegalNature",
                table: "Companies",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "NfeCredentialingConfirmed",
                table: "Companies",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "NfseAccessConfirmed",
                table: "Companies",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "NfseMunicipalityIbge",
                table: "Companies",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "TaxRegimeValidFrom",
                table: "Companies",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SerialNumber",
                table: "Certificates",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateTable(
                name: "TaxRegimeHistory",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    CompanyId = table.Column<int>(type: "INTEGER", nullable: false),
                    Crt = table.Column<int>(type: "INTEGER", nullable: true),
                    RecordedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    RecordedBy = table.Column<string>(type: "TEXT", nullable: true),
                    TaxRegime = table.Column<int>(type: "INTEGER", nullable: false),
                    ValidFrom = table.Column<DateOnly>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TaxRegimeHistory", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TaxRegimeHistory_CompanyId",
                table: "TaxRegimeHistory",
                column: "CompanyId");
        }
    }
}
