using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ClaimsModule.Persistence.Claims.Migrations
{
    /// <inheritdoc />
    public partial class AddClaimReserves : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ClaimReserveComponents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWSEQUENTIALID()"),
                    ClaimId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ComponentType = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    CurrentAmount = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    LastChangeSequence = table.Column<int>(type: "int", nullable: false),
                    RowVer = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    OrganizationEntityId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", precision: 7, nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", precision: 7, nullable: true),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UpdatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    DeletedAt = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", precision: 7, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClaimReserveComponents", x => x.Id);
                    table.UniqueConstraint("AK_ClaimReserveComponents_OrganizationEntityId_ClaimId_Id", x => new { x.OrganizationEntityId, x.ClaimId, x.Id });
                    table.ForeignKey(
                        name: "FK_ClaimReserveComponents_Claims_OrganizationEntityId_ClaimId",
                        columns: x => new { x.OrganizationEntityId, x.ClaimId },
                        principalTable: "Claims",
                        principalColumns: new[] { "OrganizationEntityId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ReserveSubmissions",
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
                    table.PrimaryKey("PK_ReserveSubmissions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ReserveSubmissions_Claims_OrganizationEntityId_ClaimId",
                        columns: x => new { x.OrganizationEntityId, x.ClaimId },
                        principalTable: "Claims",
                        principalColumns: new[] { "OrganizationEntityId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ReserveHistory",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWSEQUENTIALID()"),
                    ClaimId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ReserveId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ChangeSequence = table.Column<int>(type: "int", nullable: false),
                    TransactionType = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    PreviousBalance = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    NewBalance = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    ChangeReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    RequiredAuthority = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    RequiresManagerOverride = table.Column<bool>(type: "bit", nullable: false),
                    ApprovalStatus = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    ApprovedAt = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", precision: 7, nullable: true),
                    PostingStatus = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    PostingKey = table.Column<string>(type: "varchar(80)", unicode: false, maxLength: 80, nullable: false),
                    PostingJobId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    OrganizationEntityId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", precision: 7, nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", precision: 7, nullable: true),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UpdatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    DeletedAt = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", precision: 7, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReserveHistory", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ReserveHistory_ClaimReserveComponents_OrganizationEntityId_ClaimId_ReserveId",
                        columns: x => new { x.OrganizationEntityId, x.ClaimId, x.ReserveId },
                        principalTable: "ClaimReserveComponents",
                        principalColumns: new[] { "OrganizationEntityId", "ClaimId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ClaimReserveComponents_OrganizationEntityId_ClaimId_ComponentType",
                table: "ClaimReserveComponents",
                columns: new[] { "OrganizationEntityId", "ClaimId", "ComponentType" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ReserveHistory_OrganizationEntityId_ClaimId_ReserveId_ChangeSequence",
                table: "ReserveHistory",
                columns: new[] { "OrganizationEntityId", "ClaimId", "ReserveId", "ChangeSequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ReserveHistory_OrganizationEntityId_PostingKey",
                table: "ReserveHistory",
                columns: new[] { "OrganizationEntityId", "PostingKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ReserveSubmissions_OrganizationEntityId_ClaimId_CreatedByUserId_KeyHash",
                table: "ReserveSubmissions",
                columns: new[] { "OrganizationEntityId", "ClaimId", "CreatedByUserId", "KeyHash" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ReserveHistory");

            migrationBuilder.DropTable(
                name: "ReserveSubmissions");

            migrationBuilder.DropTable(
                name: "ClaimReserveComponents");
        }
    }
}
