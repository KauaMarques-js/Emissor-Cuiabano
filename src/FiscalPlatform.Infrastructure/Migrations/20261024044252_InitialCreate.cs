using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FiscalPlatform.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AuditLogs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    CompanyId = table.Column<int>(type: "INTEGER", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UserName = table.Column<string>(type: "TEXT", nullable: false),
                    Action = table.Column<string>(type: "TEXT", nullable: false),
                    EntityType = table.Column<string>(type: "TEXT", nullable: false),
                    EntityId = table.Column<string>(type: "TEXT", nullable: true),
                    Details = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuditLogs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Batches",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    CompanyId = table.Column<int>(type: "INTEGER", nullable: false),
                    DocumentType = table.Column<int>(type: "INTEGER", nullable: false),
                    Environment = table.Column<int>(type: "INTEGER", nullable: false),
                    Provider = table.Column<int>(type: "INTEGER", nullable: false),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    FinishedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CreatedBy = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Batches", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Certificates",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    CompanyId = table.Column<int>(type: "INTEGER", nullable: false),
                    Subject = table.Column<string>(type: "TEXT", nullable: false),
                    Issuer = table.Column<string>(type: "TEXT", nullable: false),
                    SerialNumber = table.Column<string>(type: "TEXT", nullable: false),
                    Thumbprint = table.Column<string>(type: "TEXT", nullable: false),
                    CertificateCnpj = table.Column<string>(type: "TEXT", nullable: true),
                    CnpjMatches = table.Column<bool>(type: "INTEGER", nullable: false),
                    NotBefore = table.Column<DateTime>(type: "TEXT", nullable: false),
                    NotAfter = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ImportedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ImportedBy = table.Column<string>(type: "TEXT", nullable: true),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    ReplacedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    StoredFileName = table.Column<string>(type: "TEXT", nullable: true),
                    ProtectedPassword = table.Column<string>(type: "TEXT", nullable: true),
                    ValidationNotes = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Certificates", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Companies",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Cnpj = table.Column<string>(type: "TEXT", nullable: false),
                    LegalName = table.Column<string>(type: "TEXT", nullable: false),
                    TradeName = table.Column<string>(type: "TEXT", nullable: true),
                    LegalNature = table.Column<string>(type: "TEXT", nullable: true),
                    Email = table.Column<string>(type: "TEXT", nullable: true),
                    Phone = table.Column<string>(type: "TEXT", nullable: true),
                    Address_ZipCode = table.Column<string>(type: "TEXT", nullable: true),
                    Address_Street = table.Column<string>(type: "TEXT", nullable: true),
                    Address_Number = table.Column<string>(type: "TEXT", nullable: true),
                    Address_Complement = table.Column<string>(type: "TEXT", nullable: true),
                    Address_District = table.Column<string>(type: "TEXT", nullable: true),
                    Address_City = table.Column<string>(type: "TEXT", nullable: true),
                    Address_State = table.Column<string>(type: "TEXT", nullable: true),
                    Address_CityIbgeCode = table.Column<string>(type: "TEXT", nullable: true),
                    StateRegistration = table.Column<string>(type: "TEXT", nullable: true),
                    MunicipalRegistration = table.Column<string>(type: "TEXT", nullable: true),
                    TaxRegime = table.Column<int>(type: "INTEGER", nullable: false),
                    Crt = table.Column<int>(type: "INTEGER", nullable: true),
                    Cnaes = table.Column<string>(type: "TEXT", nullable: true),
                    TaxRegimeValidFrom = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    NfeEnabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    NfeSeries = table.Column<int>(type: "INTEGER", nullable: false),
                    NfeNextNumber = table.Column<int>(type: "INTEGER", nullable: false),
                    NfeEnvironment = table.Column<int>(type: "INTEGER", nullable: false),
                    NfeProvider = table.Column<int>(type: "INTEGER", nullable: false),
                    NfeCredentialingConfirmed = table.Column<bool>(type: "INTEGER", nullable: false),
                    NfseEnabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    NfseSeries = table.Column<int>(type: "INTEGER", nullable: false),
                    NfseNextNumber = table.Column<int>(type: "INTEGER", nullable: false),
                    NfseEnvironment = table.Column<int>(type: "INTEGER", nullable: false),
                    NfseProvider = table.Column<int>(type: "INTEGER", nullable: false),
                    NfseAccessConfirmed = table.Column<bool>(type: "INTEGER", nullable: false),
                    NfseMunicipalityIbge = table.Column<string>(type: "TEXT", nullable: true),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Companies", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Customers",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    CompanyId = table.Column<int>(type: "INTEGER", nullable: false),
                    Document = table.Column<string>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    IeIndicator = table.Column<int>(type: "INTEGER", nullable: false),
                    StateRegistration = table.Column<string>(type: "TEXT", nullable: true),
                    MunicipalRegistration = table.Column<string>(type: "TEXT", nullable: true),
                    Email = table.Column<string>(type: "TEXT", nullable: true),
                    Phone = table.Column<string>(type: "TEXT", nullable: true),
                    Address_ZipCode = table.Column<string>(type: "TEXT", nullable: true),
                    Address_Street = table.Column<string>(type: "TEXT", nullable: true),
                    Address_Number = table.Column<string>(type: "TEXT", nullable: true),
                    Address_Complement = table.Column<string>(type: "TEXT", nullable: true),
                    Address_District = table.Column<string>(type: "TEXT", nullable: true),
                    Address_City = table.Column<string>(type: "TEXT", nullable: true),
                    Address_State = table.Column<string>(type: "TEXT", nullable: true),
                    Address_CityIbgeCode = table.Column<string>(type: "TEXT", nullable: true),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Customers", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Events",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    CompanyId = table.Column<int>(type: "INTEGER", nullable: false),
                    FiscalDocumentId = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Type = table.Column<string>(type: "TEXT", nullable: false),
                    Description = table.Column<string>(type: "TEXT", nullable: false),
                    UserName = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Events", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Products",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    CompanyId = table.Column<int>(type: "INTEGER", nullable: false),
                    Code = table.Column<string>(type: "TEXT", nullable: false),
                    Description = table.Column<string>(type: "TEXT", nullable: false),
                    Unit = table.Column<string>(type: "TEXT", nullable: false),
                    Ncm = table.Column<string>(type: "TEXT", nullable: true),
                    Cest = table.Column<string>(type: "TEXT", nullable: true),
                    Origin = table.Column<int>(type: "INTEGER", nullable: false),
                    Gtin = table.Column<string>(type: "TEXT", nullable: true),
                    Price = table.Column<decimal>(type: "TEXT", nullable: false),
                    Cfop = table.Column<string>(type: "TEXT", nullable: true),
                    IcmsTaxCode = table.Column<string>(type: "TEXT", nullable: true),
                    IcmsRate = table.Column<decimal>(type: "TEXT", nullable: false),
                    PisCst = table.Column<string>(type: "TEXT", nullable: true),
                    CofinsCst = table.Column<string>(type: "TEXT", nullable: true),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Products", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Services",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    CompanyId = table.Column<int>(type: "INTEGER", nullable: false),
                    Code = table.Column<string>(type: "TEXT", nullable: false),
                    Description = table.Column<string>(type: "TEXT", nullable: false),
                    NationalTaxCode = table.Column<string>(type: "TEXT", nullable: true),
                    MunicipalTaxCode = table.Column<string>(type: "TEXT", nullable: true),
                    NbsCode = table.Column<string>(type: "TEXT", nullable: true),
                    IncidenceMunicipalityIbge = table.Column<string>(type: "TEXT", nullable: true),
                    Price = table.Column<decimal>(type: "TEXT", nullable: false),
                    IssRate = table.Column<decimal>(type: "TEXT", nullable: false),
                    IssWithheld = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Services", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TaxRegimeHistory",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    CompanyId = table.Column<int>(type: "INTEGER", nullable: false),
                    TaxRegime = table.Column<int>(type: "INTEGER", nullable: false),
                    Crt = table.Column<int>(type: "INTEGER", nullable: true),
                    ValidFrom = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    RecordedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    RecordedBy = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TaxRegimeHistory", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Users",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    UserName = table.Column<string>(type: "TEXT", nullable: false),
                    PasswordHash = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Users", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Documents",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    CompanyId = table.Column<int>(type: "INTEGER", nullable: false),
                    Type = table.Column<int>(type: "INTEGER", nullable: false),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    Environment = table.Column<int>(type: "INTEGER", nullable: false),
                    Provider = table.Column<int>(type: "INTEGER", nullable: false),
                    Series = table.Column<int>(type: "INTEGER", nullable: true),
                    Number = table.Column<int>(type: "INTEGER", nullable: true),
                    CustomerId = table.Column<int>(type: "INTEGER", nullable: true),
                    IssuerSnapshotJson = table.Column<string>(type: "TEXT", nullable: true),
                    CustomerSnapshotJson = table.Column<string>(type: "TEXT", nullable: true),
                    Nfe_NatureOfOperation = table.Column<string>(type: "TEXT", nullable: true),
                    Nfe_OperationType = table.Column<int>(type: "INTEGER", nullable: false),
                    Nfe_Purpose = table.Column<int>(type: "INTEGER", nullable: false),
                    Nfe_FreightMode = table.Column<int>(type: "INTEGER", nullable: false),
                    Nfe_Freight = table.Column<decimal>(type: "TEXT", nullable: false),
                    Nfe_Insurance = table.Column<decimal>(type: "TEXT", nullable: false),
                    Nfe_OtherExpenses = table.Column<decimal>(type: "TEXT", nullable: false),
                    Nfse_CompetenceDate = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    Nfse_IncidenceMunicipalityIbge = table.Column<string>(type: "TEXT", nullable: true),
                    Nfse_IssWithheld = table.Column<bool>(type: "INTEGER", nullable: false),
                    AdditionalInfo = table.Column<string>(type: "TEXT", nullable: true),
                    TotalItems = table.Column<decimal>(type: "TEXT", nullable: false),
                    TotalDiscount = table.Column<decimal>(type: "TEXT", nullable: false),
                    TotalTaxes = table.Column<decimal>(type: "TEXT", nullable: false),
                    TotalAmount = table.Column<decimal>(type: "TEXT", nullable: false),
                    AccessKey = table.Column<string>(type: "TEXT", nullable: true),
                    Protocol = table.Column<string>(type: "TEXT", nullable: true),
                    OfficialNumber = table.Column<string>(type: "TEXT", nullable: true),
                    VerificationCode = table.Column<string>(type: "TEXT", nullable: true),
                    AuthorizedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    XmlPath = table.Column<string>(type: "TEXT", nullable: true),
                    LastErrorCode = table.Column<string>(type: "TEXT", nullable: true),
                    LastErrorMessage = table.Column<string>(type: "TEXT", nullable: true),
                    CancelledAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CancelReason = table.Column<string>(type: "TEXT", nullable: true),
                    CancelProtocol = table.Column<string>(type: "TEXT", nullable: true),
                    CorrectionLetterCount = table.Column<int>(type: "INTEGER", nullable: false),
                    IdempotencyKey = table.Column<Guid>(type: "TEXT", nullable: false),
                    SourceDocumentId = table.Column<int>(type: "INTEGER", nullable: true),
                    SubstitutesDocumentId = table.Column<int>(type: "INTEGER", nullable: true),
                    SubstitutedByDocumentId = table.Column<int>(type: "INTEGER", nullable: true),
                    LastBatchId = table.Column<int>(type: "INTEGER", nullable: true),
                    Archived = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Documents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Documents_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Documents_Customers_CustomerId",
                        column: x => x.CustomerId,
                        principalTable: "Customers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Attempts",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    CompanyId = table.Column<int>(type: "INTEGER", nullable: false),
                    FiscalDocumentId = table.Column<int>(type: "INTEGER", nullable: false),
                    BatchId = table.Column<int>(type: "INTEGER", nullable: true),
                    OperationId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Operation = table.Column<int>(type: "INTEGER", nullable: false),
                    DocumentType = table.Column<int>(type: "INTEGER", nullable: false),
                    Environment = table.Column<int>(type: "INTEGER", nullable: false),
                    Provider = table.Column<int>(type: "INTEGER", nullable: false),
                    StartedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    FinishedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    Outcome = table.Column<int>(type: "INTEGER", nullable: false),
                    Code = table.Column<string>(type: "TEXT", nullable: true),
                    Message = table.Column<string>(type: "TEXT", nullable: true),
                    Details = table.Column<string>(type: "TEXT", nullable: true),
                    Protocol = table.Column<string>(type: "TEXT", nullable: true),
                    RequestPayload = table.Column<string>(type: "TEXT", nullable: true),
                    ResponsePayload = table.Column<string>(type: "TEXT", nullable: true),
                    Summary = table.Column<string>(type: "TEXT", nullable: true),
                    UserName = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Attempts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Attempts_Documents_FiscalDocumentId",
                        column: x => x.FiscalDocumentId,
                        principalTable: "Documents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "BatchItems",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    BatchId = table.Column<int>(type: "INTEGER", nullable: false),
                    FiscalDocumentId = table.Column<int>(type: "INTEGER", nullable: false),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    Message = table.Column<string>(type: "TEXT", nullable: true),
                    ProcessedAt = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BatchItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BatchItems_Batches_BatchId",
                        column: x => x.BatchId,
                        principalTable: "Batches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_BatchItems_Documents_FiscalDocumentId",
                        column: x => x.FiscalDocumentId,
                        principalTable: "Documents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "DocumentItems",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    FiscalDocumentId = table.Column<int>(type: "INTEGER", nullable: false),
                    LineNumber = table.Column<int>(type: "INTEGER", nullable: false),
                    ProductId = table.Column<int>(type: "INTEGER", nullable: true),
                    ServiceId = table.Column<int>(type: "INTEGER", nullable: true),
                    Code = table.Column<string>(type: "TEXT", nullable: false),
                    Description = table.Column<string>(type: "TEXT", nullable: false),
                    Unit = table.Column<string>(type: "TEXT", nullable: true),
                    Ncm = table.Column<string>(type: "TEXT", nullable: true),
                    Cest = table.Column<string>(type: "TEXT", nullable: true),
                    Cfop = table.Column<string>(type: "TEXT", nullable: true),
                    Origin = table.Column<int>(type: "INTEGER", nullable: false),
                    Gtin = table.Column<string>(type: "TEXT", nullable: true),
                    IcmsTaxCode = table.Column<string>(type: "TEXT", nullable: true),
                    PisCst = table.Column<string>(type: "TEXT", nullable: true),
                    CofinsCst = table.Column<string>(type: "TEXT", nullable: true),
                    NationalTaxCode = table.Column<string>(type: "TEXT", nullable: true),
                    MunicipalTaxCode = table.Column<string>(type: "TEXT", nullable: true),
                    NbsCode = table.Column<string>(type: "TEXT", nullable: true),
                    Quantity = table.Column<decimal>(type: "TEXT", nullable: false),
                    UnitPrice = table.Column<decimal>(type: "TEXT", nullable: false),
                    Discount = table.Column<decimal>(type: "TEXT", nullable: false),
                    TaxRate = table.Column<decimal>(type: "TEXT", nullable: false),
                    GrossAmount = table.Column<decimal>(type: "TEXT", nullable: false),
                    Total = table.Column<decimal>(type: "TEXT", nullable: false),
                    TaxValue = table.Column<decimal>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DocumentItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DocumentItems_Documents_FiscalDocumentId",
                        column: x => x.FiscalDocumentId,
                        principalTable: "Documents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Attempts_CompanyId_StartedAt",
                table: "Attempts",
                columns: new[] { "CompanyId", "StartedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Attempts_FiscalDocumentId",
                table: "Attempts",
                column: "FiscalDocumentId");

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogs_CompanyId_CreatedAt",
                table: "AuditLogs",
                columns: new[] { "CompanyId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_BatchItems_BatchId",
                table: "BatchItems",
                column: "BatchId");

            migrationBuilder.CreateIndex(
                name: "IX_BatchItems_FiscalDocumentId",
                table: "BatchItems",
                column: "FiscalDocumentId");

            migrationBuilder.CreateIndex(
                name: "IX_Certificates_CompanyId_IsActive",
                table: "Certificates",
                columns: new[] { "CompanyId", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_Companies_Cnpj",
                table: "Companies",
                column: "Cnpj",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Customers_CompanyId_Document",
                table: "Customers",
                columns: new[] { "CompanyId", "Document" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DocumentItems_FiscalDocumentId",
                table: "DocumentItems",
                column: "FiscalDocumentId");

            migrationBuilder.CreateIndex(
                name: "IX_Documents_CompanyId_Type_Status",
                table: "Documents",
                columns: new[] { "CompanyId", "Type", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_Documents_CustomerId",
                table: "Documents",
                column: "CustomerId");

            migrationBuilder.CreateIndex(
                name: "IX_Documents_IdempotencyKey",
                table: "Documents",
                column: "IdempotencyKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Events_FiscalDocumentId",
                table: "Events",
                column: "FiscalDocumentId");

            migrationBuilder.CreateIndex(
                name: "IX_Products_CompanyId_Code",
                table: "Products",
                columns: new[] { "CompanyId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Services_CompanyId_Code",
                table: "Services",
                columns: new[] { "CompanyId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TaxRegimeHistory_CompanyId",
                table: "TaxRegimeHistory",
                column: "CompanyId");

            migrationBuilder.CreateIndex(
                name: "IX_Users_UserName",
                table: "Users",
                column: "UserName",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Attempts");

            migrationBuilder.DropTable(
                name: "AuditLogs");

            migrationBuilder.DropTable(
                name: "BatchItems");

            migrationBuilder.DropTable(
                name: "Certificates");

            migrationBuilder.DropTable(
                name: "DocumentItems");

            migrationBuilder.DropTable(
                name: "Events");

            migrationBuilder.DropTable(
                name: "Products");

            migrationBuilder.DropTable(
                name: "Services");

            migrationBuilder.DropTable(
                name: "TaxRegimeHistory");

            migrationBuilder.DropTable(
                name: "Users");

            migrationBuilder.DropTable(
                name: "Batches");

            migrationBuilder.DropTable(
                name: "Documents");

            migrationBuilder.DropTable(
                name: "Companies");

            migrationBuilder.DropTable(
                name: "Customers");
        }
    }
}
