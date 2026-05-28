using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nexus.Patients.Api.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialPatientSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PatientCodeSequences",
                columns: table => new
                {
                    BranchCode = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    Year = table.Column<short>(type: "smallint", nullable: false),
                    LastValue = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PatientCodeSequences", x => new { x.BranchCode, x.Year });
                });

            migrationBuilder.CreateTable(
                name: "Patients",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PublicCode = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    FullName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Phone = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    Email = table.Column<string>(type: "nvarchar(254)", maxLength: 254, nullable: true),
                    DateOfBirth = table.Column<DateOnly>(type: "date", nullable: false),
                    Gender = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    Status = table.Column<byte>(type: "tinyint", nullable: false),
                    ArchivedAtUtc = table.Column<DateTime>(type: "datetime2(7)", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2(7)", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2(7)", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Patients", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PendingAuditEntries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IdempotencyKey = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    PayloadJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Attempts = table.Column<int>(type: "int", nullable: false),
                    NextRetryUtc = table.Column<DateTime>(type: "datetime2(7)", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2(7)", nullable: false),
                    LastError = table.Column<string>(type: "nvarchar(1024)", maxLength: 1024, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PendingAuditEntries", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PatientBranchLinks",
                columns: table => new
                {
                    PatientId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BranchId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IsPrimary = table.Column<bool>(type: "bit", nullable: false),
                    LinkedAtUtc = table.Column<DateTime>(type: "datetime2(7)", nullable: false),
                    UnlinkedAtUtc = table.Column<DateTime>(type: "datetime2(7)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PatientBranchLinks", x => new { x.PatientId, x.BranchId });
                    table.ForeignKey(
                        name: "FK_PatientBranchLinks_Patients_PatientId",
                        column: x => x.PatientId,
                        principalTable: "Patients",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PatientBranchLinks_PatientId_IsPrimary_Unlinked",
                table: "PatientBranchLinks",
                columns: new[] { "PatientId", "IsPrimary", "UnlinkedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_Patients_Status_ArchivedAtUtc",
                table: "Patients",
                columns: new[] { "Status", "ArchivedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "UX_Patients_Email_DOB",
                table: "Patients",
                columns: new[] { "Email", "DateOfBirth" },
                unique: true,
                filter: "[Email] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "UX_Patients_Phone_DOB",
                table: "Patients",
                columns: new[] { "Phone", "DateOfBirth" },
                unique: true,
                filter: "[Phone] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "UX_Patients_PublicCode",
                table: "Patients",
                column: "PublicCode",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PendingAudit_NextRetryUtc",
                table: "PendingAuditEntries",
                column: "NextRetryUtc");

            migrationBuilder.CreateIndex(
                name: "UX_PendingAudit_IdempotencyKey",
                table: "PendingAuditEntries",
                column: "IdempotencyKey",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PatientBranchLinks");

            migrationBuilder.DropTable(
                name: "PatientCodeSequences");

            migrationBuilder.DropTable(
                name: "PendingAuditEntries");

            migrationBuilder.DropTable(
                name: "Patients");
        }
    }
}
