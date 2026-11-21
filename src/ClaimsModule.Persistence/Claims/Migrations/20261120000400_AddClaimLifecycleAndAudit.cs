using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace ClaimsModule.Persistence.Claims.Migrations
{
    /// <inheritdoc />
    public partial class AddClaimLifecycleAndAudit : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ClaimAuditLog_OrganizationEntityId_ClaimId_CreatedAt",
                table: "ClaimAuditLog");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ClosedAt",
                table: "Claims",
                type: "datetimeoffset(7)",
                precision: 7,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ClosureReason",
                table: "Claims",
                type: "nvarchar(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Position",
                table: "ClaimAuditLog",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "RelatedEntityType",
                table: "ClaimAuditLog",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ClaimStatusSubmissions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWSEQUENTIALID()"),
                    ClaimId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    KeyHash = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false),
                    RequestHash = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false),
                    ResponseJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    OrganizationEntityId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", precision: 7, nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", precision: 7, nullable: true),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UpdatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    DeletedAt = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", precision: 7, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClaimStatusSubmissions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ClaimStatusSubmissions_Claims_OrganizationEntityId_ClaimId",
                        columns: x => new { x.OrganizationEntityId, x.ClaimId },
                        principalTable: "Claims",
                        principalColumns: new[] { "OrganizationEntityId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ClaimStatusTransitions",
                columns: table => new
                {
                    FromStatus = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    ToStatus = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    RequiredPermission = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClaimStatusTransitions", x => new { x.FromStatus, x.ToStatus });
                });

            migrationBuilder.InsertData(
                table: "ClaimStatusTransitions",
                columns: new[] { "FromStatus", "ToStatus", "RequiredPermission" },
                values: new object[,]
                {
                    { "Closed", "Reopened", "supervisor" },
                    { "Draft", "Open", "handler" },
                    { "Open", "Closed", "handler" },
                    { "Open", "PendingPayment", "handler" },
                    { "Open", "UnderInvestigation", "handler" },
                    { "Open", "Withdrawn", "handler" },
                    { "PendingPayment", "Closed", "handler" },
                    { "Reopened", "Open", "system" },
                    { "UnderInvestigation", "Closed", "handler" },
                    { "UnderInvestigation", "Open", "handler" },
                    { "UnderInvestigation", "PendingPayment", "handler" },
                    { "UnderInvestigation", "Withdrawn", "handler" }
                });

            migrationBuilder.CreateIndex(
                name: "IX_ClaimAuditLog_OrganizationEntityId_ClaimId_CreatedAt_Position_Id",
                table: "ClaimAuditLog",
                columns: new[] { "OrganizationEntityId", "ClaimId", "CreatedAt", "Position", "Id" },
                descending: new[] { false, false, true, true, true });

            migrationBuilder.CreateIndex(
                name: "IX_ClaimStatusSubmissions_OrganizationEntityId_ClaimId_CreatedByUserId_KeyHash",
                table: "ClaimStatusSubmissions",
                columns: new[] { "OrganizationEntityId", "ClaimId", "CreatedByUserId", "KeyHash" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ClaimStatusSubmissions");

            migrationBuilder.DropTable(
                name: "ClaimStatusTransitions");

            migrationBuilder.DropIndex(
                name: "IX_ClaimAuditLog_OrganizationEntityId_ClaimId_CreatedAt_Position_Id",
                table: "ClaimAuditLog");

            migrationBuilder.DropColumn(
                name: "ClosedAt",
                table: "Claims");

            migrationBuilder.DropColumn(
                name: "ClosureReason",
                table: "Claims");

            migrationBuilder.DropColumn(
                name: "Position",
                table: "ClaimAuditLog");

            migrationBuilder.DropColumn(
                name: "RelatedEntityType",
                table: "ClaimAuditLog");

            migrationBuilder.CreateIndex(
                name: "IX_ClaimAuditLog_OrganizationEntityId_ClaimId_CreatedAt",
                table: "ClaimAuditLog",
                columns: new[] { "OrganizationEntityId", "ClaimId", "CreatedAt" });
        }
    }
}
