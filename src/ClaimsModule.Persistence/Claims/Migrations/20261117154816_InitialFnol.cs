using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace ClaimsModule.Persistence.Claims.Migrations
{
    /// <inheritdoc />
    public partial class InitialFnol : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CauseOfLossCodes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWSEQUENTIALID()"),
                    Code = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    PerilCategory = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
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
                    table.PrimaryKey("PK_CauseOfLossCodes", x => x.Id);
                    table.UniqueConstraint("AK_CauseOfLossCodes_OrganizationEntityId_Code", x => new { x.OrganizationEntityId, x.Code });
                });

            migrationBuilder.CreateTable(
                name: "ClaimNumberCounters",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWSEQUENTIALID()"),
                    Year = table.Column<int>(type: "int", nullable: false),
                    LastIssued = table.Column<int>(type: "int", nullable: false),
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
                    table.PrimaryKey("PK_ClaimNumberCounters", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Policies",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWSEQUENTIALID()"),
                    PolicyNumber = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    ClientName = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    EffectiveDate = table.Column<DateOnly>(type: "date", nullable: false),
                    ExpirationDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    CoverageTypes = table.Column<string>(type: "nvarchar(max)", nullable: false),
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
                    table.PrimaryKey("PK_Policies", x => x.Id);
                    table.UniqueConstraint("AK_Policies_OrganizationEntityId_Id", x => new { x.OrganizationEntityId, x.Id });
                });

            migrationBuilder.CreateTable(
                name: "Claims",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWSEQUENTIALID()"),
                    ClaimNumber = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    ClaimType = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    PolicyId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    PolicyNumber = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    ClientName = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true),
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
                    table.PrimaryKey("PK_Claims", x => x.Id);
                    table.UniqueConstraint("AK_Claims_OrganizationEntityId_Id", x => new { x.OrganizationEntityId, x.Id });
                    table.ForeignKey(
                        name: "FK_Claims_Policies_OrganizationEntityId_PolicyId",
                        columns: x => new { x.OrganizationEntityId, x.PolicyId },
                        principalTable: "Policies",
                        principalColumns: new[] { "OrganizationEntityId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ClaimAuditLog",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWSEQUENTIALID()"),
                    ClaimId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CorrelationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EventType = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    OldValues = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    NewValues = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RelatedEntityId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
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
                    table.PrimaryKey("PK_ClaimAuditLog", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ClaimAuditLog_Claims_OrganizationEntityId_ClaimId",
                        columns: x => new { x.OrganizationEntityId, x.ClaimId },
                        principalTable: "Claims",
                        principalColumns: new[] { "OrganizationEntityId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ClaimParties",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWSEQUENTIALID()"),
                    ClaimId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Role = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    PartyType = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    FirstName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    LastName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    CompanyName = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true),
                    Email = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true),
                    Phone = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    Notes = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
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
                    table.PrimaryKey("PK_ClaimParties", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ClaimParties_Claims_OrganizationEntityId_ClaimId",
                        columns: x => new { x.OrganizationEntityId, x.ClaimId },
                        principalTable: "Claims",
                        principalColumns: new[] { "OrganizationEntityId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ClaimRiskObjects",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWSEQUENTIALID()"),
                    ClaimId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AssetType = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    AssetDescription = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    DamageDescription = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    AssetReference = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true),
                    IsPrimary = table.Column<bool>(type: "bit", nullable: false),
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
                    table.PrimaryKey("PK_ClaimRiskObjects", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ClaimRiskObjects_Claims_OrganizationEntityId_ClaimId",
                        columns: x => new { x.OrganizationEntityId, x.ClaimId },
                        principalTable: "Claims",
                        principalColumns: new[] { "OrganizationEntityId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ClaimSubmissions",
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
                    table.PrimaryKey("PK_ClaimSubmissions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ClaimSubmissions_Claims_OrganizationEntityId_ClaimId",
                        columns: x => new { x.OrganizationEntityId, x.ClaimId },
                        principalTable: "Claims",
                        principalColumns: new[] { "OrganizationEntityId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ClaimWarnings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWSEQUENTIALID()"),
                    ClaimId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Field = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Message = table.Column<string>(type: "nvarchar(max)", nullable: false),
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
                    table.PrimaryKey("PK_ClaimWarnings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ClaimWarnings_Claims_OrganizationEntityId_ClaimId",
                        columns: x => new { x.OrganizationEntityId, x.ClaimId },
                        principalTable: "Claims",
                        principalColumns: new[] { "OrganizationEntityId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "LossEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWSEQUENTIALID()"),
                    ClaimId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LossDate = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", precision: 7, nullable: false),
                    CauseOfLossCode = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    LossDescription = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    LossLocation = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    EstimatedLossAmount = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: true),
                    PoliceReportNumber = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
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
                    table.PrimaryKey("PK_LossEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LossEvents_CauseOfLossCodes_OrganizationEntityId_CauseOfLossCode",
                        columns: x => new { x.OrganizationEntityId, x.CauseOfLossCode },
                        principalTable: "CauseOfLossCodes",
                        principalColumns: new[] { "OrganizationEntityId", "Code" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_LossEvents_Claims_OrganizationEntityId_ClaimId",
                        columns: x => new { x.OrganizationEntityId, x.ClaimId },
                        principalTable: "Claims",
                        principalColumns: new[] { "OrganizationEntityId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.Sql("""
                EXEC(N'CREATE TRIGGER dbo.TR_ClaimAuditLog_AppendOnly ON dbo.ClaimAuditLog
                    INSTEAD OF UPDATE, DELETE
                    AS
                    BEGIN
                        SET NOCOUNT ON;
                        THROW 50002, ''Claim audit entries are append-only.'', 1;
                    END');
                """);

            migrationBuilder.InsertData(
                table: "CauseOfLossCodes",
                columns: new[] { "Id", "Code", "CreatedAt", "CreatedByUserId", "DeletedAt", "IsActive", "Name", "OrganizationEntityId", "PerilCategory", "UpdatedAt", "UpdatedByUserId" },
                values: new object[,]
                {
                    { new Guid("235ba2a1-dbc7-456a-8da0-000000000001"), "COL-FIRE", new DateTimeOffset(new DateTime(2026, 5, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, null, true, "Fire", new Guid("61fd4d23-97a2-4c4a-ace1-96a3b3e738d4"), "Property", null, null },
                    { new Guid("235ba2a1-dbc7-456a-8da0-000000000002"), "COL-FLOOD", new DateTimeOffset(new DateTime(2026, 5, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, null, true, "Flood", new Guid("61fd4d23-97a2-4c4a-ace1-96a3b3e738d4"), "Weather", null, null },
                    { new Guid("235ba2a1-dbc7-456a-8da0-000000000003"), "COL-THEFT", new DateTimeOffset(new DateTime(2026, 5, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, null, true, "Theft", new Guid("61fd4d23-97a2-4c4a-ace1-96a3b3e738d4"), "Crime", null, null },
                    { new Guid("235ba2a1-dbc7-456a-8da0-000000000004"), "COL-VEH-COL", new DateTimeOffset(new DateTime(2026, 5, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, null, true, "Vehicle Collision", new Guid("61fd4d23-97a2-4c4a-ace1-96a3b3e738d4"), "Auto", null, null },
                    { new Guid("235ba2a1-dbc7-456a-8da0-000000000005"), "COL-VEH-COMP", new DateTimeOffset(new DateTime(2026, 5, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, null, true, "Vehicle Comprehensive", new Guid("61fd4d23-97a2-4c4a-ace1-96a3b3e738d4"), "Auto", null, null },
                    { new Guid("235ba2a1-dbc7-456a-8da0-000000000006"), "COL-LIAB", new DateTimeOffset(new DateTime(2026, 5, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, null, true, "Third Party Liability", new Guid("61fd4d23-97a2-4c4a-ace1-96a3b3e738d4"), "Liability", null, null },
                    { new Guid("235ba2a1-dbc7-456a-8da0-000000000007"), "COL-EQUIP", new DateTimeOffset(new DateTime(2026, 5, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, null, true, "Equipment Breakdown", new Guid("61fd4d23-97a2-4c4a-ace1-96a3b3e738d4"), "Equipment", null, null },
                    { new Guid("235ba2a1-dbc7-456a-8da0-000000000008"), "COL-WIND", new DateTimeOffset(new DateTime(2026, 5, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, null, true, "Wind / Storm", new Guid("61fd4d23-97a2-4c4a-ace1-96a3b3e738d4"), "Weather", null, null },
                    { new Guid("235ba2a1-dbc7-456a-8da0-000000000009"), "COL-INJURY", new DateTimeOffset(new DateTime(2026, 5, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, null, true, "Bodily Injury", new Guid("61fd4d23-97a2-4c4a-ace1-96a3b3e738d4"), "Liability", null, null },
                    { new Guid("235ba2a1-dbc7-456a-8da0-000000000010"), "COL-OTHER", new DateTimeOffset(new DateTime(2026, 5, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, null, true, "Other / Unknown", new Guid("61fd4d23-97a2-4c4a-ace1-96a3b3e738d4"), "General", null, null }
                });

            migrationBuilder.InsertData(
                table: "Policies",
                columns: new[] { "Id", "ClientName", "CoverageTypes", "CreatedAt", "CreatedByUserId", "DeletedAt", "EffectiveDate", "ExpirationDate", "OrganizationEntityId", "PolicyNumber", "Status", "UpdatedAt", "UpdatedByUserId" },
                values: new object[,]
                {
                    { new Guid("1a46cfff-f909-44dc-ad15-000000000001"), "Meridian Transport LLC", "Vehicle,Cargo", new DateTimeOffset(new DateTime(2026, 5, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, null, new DateOnly(2024, 1, 1), new DateOnly(2026, 12, 31), new Guid("61fd4d23-97a2-4c4a-ace1-96a3b3e738d4"), "POL-2024-001001", "Active", null, null },
                    { new Guid("1a46cfff-f909-44dc-ad15-000000000002"), "Harborview Properties Inc", "Property,Liability", new DateTimeOffset(new DateTime(2026, 5, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, null, new DateOnly(2024, 6, 1), new DateOnly(2026, 5, 31), new Guid("61fd4d23-97a2-4c4a-ace1-96a3b3e738d4"), "POL-2024-001002", "Expired", null, null },
                    { new Guid("1a46cfff-f909-44dc-ad15-000000000003"), "Coastal Builders Group", "Property,Equipment", new DateTimeOffset(new DateTime(2026, 5, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, null, new DateOnly(2025, 3, 1), new DateOnly(2027, 2, 28), new Guid("61fd4d23-97a2-4c4a-ace1-96a3b3e738d4"), "POL-2025-002001", "Active", null, null },
                    { new Guid("1a46cfff-f909-44dc-ad15-000000000004"), "Stanton Medical Group", "Liability,Vehicle", new DateTimeOffset(new DateTime(2026, 5, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, null, new DateOnly(2025, 1, 1), new DateOnly(2026, 12, 31), new Guid("61fd4d23-97a2-4c4a-ace1-96a3b3e738d4"), "POL-2025-002002", "Active", null, null },
                    { new Guid("1a46cfff-f909-44dc-ad15-000000000005"), "Archived Corp", "Property", new DateTimeOffset(new DateTime(2026, 5, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, null, new DateOnly(2020, 1, 1), new DateOnly(2021, 12, 31), new Guid("61fd4d23-97a2-4c4a-ace1-96a3b3e738d4"), "POL-2023-000099", "Expired", null, null }
                });

            migrationBuilder.CreateIndex(
                name: "IX_ClaimAuditLog_OrganizationEntityId_ClaimId_CreatedAt",
                table: "ClaimAuditLog",
                columns: new[] { "OrganizationEntityId", "ClaimId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ClaimNumberCounters_OrganizationEntityId_Year",
                table: "ClaimNumberCounters",
                columns: new[] { "OrganizationEntityId", "Year" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ClaimParties_OrganizationEntityId_ClaimId",
                table: "ClaimParties",
                columns: new[] { "OrganizationEntityId", "ClaimId" });

            migrationBuilder.CreateIndex(
                name: "IX_ClaimRiskObjects_OrganizationEntityId_ClaimId",
                table: "ClaimRiskObjects",
                columns: new[] { "OrganizationEntityId", "ClaimId" });

            migrationBuilder.CreateIndex(
                name: "IX_Claims_OrganizationEntityId_ClaimNumber",
                table: "Claims",
                columns: new[] { "OrganizationEntityId", "ClaimNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Claims_OrganizationEntityId_PolicyId",
                table: "Claims",
                columns: new[] { "OrganizationEntityId", "PolicyId" });

            migrationBuilder.CreateIndex(
                name: "IX_ClaimSubmissions_OrganizationEntityId_ClaimId",
                table: "ClaimSubmissions",
                columns: new[] { "OrganizationEntityId", "ClaimId" });

            migrationBuilder.CreateIndex(
                name: "IX_ClaimSubmissions_OrganizationEntityId_CreatedByUserId_KeyHash",
                table: "ClaimSubmissions",
                columns: new[] { "OrganizationEntityId", "CreatedByUserId", "KeyHash" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ClaimWarnings_OrganizationEntityId_ClaimId",
                table: "ClaimWarnings",
                columns: new[] { "OrganizationEntityId", "ClaimId" });

            migrationBuilder.CreateIndex(
                name: "IX_LossEvents_OrganizationEntityId_CauseOfLossCode",
                table: "LossEvents",
                columns: new[] { "OrganizationEntityId", "CauseOfLossCode" });

            migrationBuilder.CreateIndex(
                name: "IX_LossEvents_OrganizationEntityId_ClaimId",
                table: "LossEvents",
                columns: new[] { "OrganizationEntityId", "ClaimId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Policies_OrganizationEntityId_PolicyNumber",
                table: "Policies",
                columns: new[] { "OrganizationEntityId", "PolicyNumber" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ClaimAuditLog");

            migrationBuilder.DropTable(
                name: "ClaimNumberCounters");

            migrationBuilder.DropTable(
                name: "ClaimParties");

            migrationBuilder.DropTable(
                name: "ClaimRiskObjects");

            migrationBuilder.DropTable(
                name: "ClaimSubmissions");

            migrationBuilder.DropTable(
                name: "ClaimWarnings");

            migrationBuilder.DropTable(
                name: "LossEvents");

            migrationBuilder.DropTable(
                name: "CauseOfLossCodes");

            migrationBuilder.DropTable(
                name: "Claims");

            migrationBuilder.DropTable(
                name: "Policies");
        }
    }
}
