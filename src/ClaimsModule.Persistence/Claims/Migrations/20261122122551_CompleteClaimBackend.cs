using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ClaimsModule.Persistence.Claims.Migrations
{
    /// <inheritdoc />
    public partial class CompleteClaimBackend : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                IF DATABASE_PRINCIPAL_ID(N'ClaimsAuditWriter') IS NULL
                    EXEC(N'CREATE ROLE [ClaimsAuditWriter] AUTHORIZATION [dbo];');
                ELSE IF NOT EXISTS (SELECT 1 FROM sys.database_principals WHERE name = N'ClaimsAuditWriter' AND type = 'R')
                    THROW 50006, 'ClaimsAuditWriter must be a database role.', 1;

                GRANT SELECT, INSERT ON OBJECT::[dbo].[ClaimAuditLog] TO [ClaimsAuditWriter];
                DENY UPDATE, DELETE ON OBJECT::[dbo].[ClaimAuditLog] TO [ClaimsAuditWriter];
                DECLARE @AuditColumns nvarchar(max) =
                    (SELECT STRING_AGG(CONVERT(nvarchar(max), QUOTENAME(name)), N',')
                     FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.ClaimAuditLog'));
                DECLARE @DenyAuditColumns nvarchar(max) =
                    N'DENY UPDATE (' + @AuditColumns + N') ON OBJECT::[dbo].[ClaimAuditLog] TO [ClaimsAuditWriter];';
                EXEC sys.sp_executesql @DenyAuditColumns;
                """);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "LastSlaBreachAt",
                table: "Claims",
                type: "datetimeoffset(7)",
                precision: 7,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Notes",
                table: "Claims",
                type: "nvarchar(4000)",
                maxLength: 4000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Severity",
                table: "Claims",
                type: "nvarchar(30)",
                maxLength: 30,
                nullable: false,
                defaultValue: "Standard");

            migrationBuilder.CreateTable(
                name: "ClaimDocuments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWSEQUENTIALID()"),
                    ClaimId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FileName = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    StorageKey = table.Column<string>(type: "varchar(255)", unicode: false, maxLength: 255, nullable: false),
                    StorageProvider = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    ContentType = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    SizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    DocumentType = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Notes = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
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
                    table.PrimaryKey("PK_ClaimDocuments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ClaimDocuments_Claims_OrganizationEntityId_ClaimId",
                        columns: x => new { x.OrganizationEntityId, x.ClaimId },
                        principalTable: "Claims",
                        principalColumns: new[] { "OrganizationEntityId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ClaimMutationSubmissions",
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
                    table.PrimaryKey("PK_ClaimMutationSubmissions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ClaimMutationSubmissions_Claims_OrganizationEntityId_ClaimId",
                        columns: x => new { x.OrganizationEntityId, x.ClaimId },
                        principalTable: "Claims",
                        principalColumns: new[] { "OrganizationEntityId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Claims_OrganizationEntityId_Status_Id",
                table: "Claims",
                columns: new[] { "OrganizationEntityId", "Status", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_ClaimDocuments_OrganizationEntityId_ClaimId_CreatedAt",
                table: "ClaimDocuments",
                columns: new[] { "OrganizationEntityId", "ClaimId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ClaimDocuments_OrganizationEntityId_StorageKey",
                table: "ClaimDocuments",
                columns: new[] { "OrganizationEntityId", "StorageKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ClaimMutationSubmissions_OrganizationEntityId_ClaimId_CreatedByUserId_KeyHash",
                table: "ClaimMutationSubmissions",
                columns: new[] { "OrganizationEntityId", "ClaimId", "CreatedByUserId", "KeyHash" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                IF DATABASE_PRINCIPAL_ID(N'ClaimsAuditWriter') IS NOT NULL
                BEGIN
                    DECLARE @AuditColumns nvarchar(max) =
                        (SELECT STRING_AGG(CONVERT(nvarchar(max), QUOTENAME(name)), N',')
                         FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.ClaimAuditLog'));
                    DECLARE @RevokeAuditColumns nvarchar(max) =
                        N'REVOKE UPDATE (' + @AuditColumns + N') ON OBJECT::[dbo].[ClaimAuditLog] FROM [ClaimsAuditWriter];';
                    EXEC sys.sp_executesql @RevokeAuditColumns;
                    REVOKE SELECT, INSERT, UPDATE, DELETE ON OBJECT::[dbo].[ClaimAuditLog] FROM [ClaimsAuditWriter];
                    IF NOT EXISTS (SELECT 1 FROM sys.database_role_members
                                   WHERE role_principal_id = DATABASE_PRINCIPAL_ID(N'ClaimsAuditWriter'))
                        DROP ROLE [ClaimsAuditWriter];
                END;
                """);
            migrationBuilder.DropTable(
                name: "ClaimDocuments");

            migrationBuilder.DropTable(
                name: "ClaimMutationSubmissions");

            migrationBuilder.DropIndex(
                name: "IX_Claims_OrganizationEntityId_Status_Id",
                table: "Claims");

            migrationBuilder.DropColumn(
                name: "LastSlaBreachAt",
                table: "Claims");

            migrationBuilder.DropColumn(
                name: "Notes",
                table: "Claims");

            migrationBuilder.DropColumn(
                name: "Severity",
                table: "Claims");
        }
    }
}
