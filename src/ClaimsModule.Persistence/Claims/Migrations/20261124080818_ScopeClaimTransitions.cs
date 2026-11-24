using Microsoft.EntityFrameworkCore.Migrations;

namespace ClaimsModule.Persistence.Claims.Migrations;

public partial class ScopeClaimTransitions : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<Guid>("Id", "ClaimStatusTransitions", "uniqueidentifier",
            nullable: false, defaultValueSql: "NEWSEQUENTIALID()");
        migrationBuilder.AddColumn<Guid>("OrganizationEntityId", "ClaimStatusTransitions", "uniqueidentifier",
            nullable: true);
        migrationBuilder.AddColumn<DateTimeOffset>("CreatedAt", "ClaimStatusTransitions", "datetimeoffset(7)",
            precision: 7, nullable: true);
        migrationBuilder.AddColumn<Guid>("CreatedByUserId", "ClaimStatusTransitions", "uniqueidentifier",
            nullable: true);
        migrationBuilder.AddColumn<DateTimeOffset>("UpdatedAt", "ClaimStatusTransitions", "datetimeoffset(7)",
            precision: 7, nullable: true);
        migrationBuilder.AddColumn<Guid>("UpdatedByUserId", "ClaimStatusTransitions", "uniqueidentifier",
            nullable: true);
        migrationBuilder.AddColumn<bool>("IsDeleted", "ClaimStatusTransitions", "bit",
            nullable: false, defaultValue: false);
        migrationBuilder.AddColumn<DateTimeOffset>("DeletedAt", "ClaimStatusTransitions", "datetimeoffset(7)",
            precision: 7, nullable: true);

        // Keep permissions from the previous table and match the model's seed IDs.
        migrationBuilder.Sql("""
            EXEC(N'
                UPDATE transition
                SET [OrganizationEntityId] = ''61fd4d23-97a2-4c4a-ace1-96a3b3e738d4'',
                    [CreatedAt] = ''2026-05-01T00:00:00+00:00'',
                    [Id] = COALESCE(seed.[Id], transition.[Id])
                FROM [ClaimStatusTransitions] AS transition
                LEFT JOIN (VALUES
                    (''Draft'', ''Open'', ''f44ba162-e62d-4f9e-9c25-000000000001''),
                    (''Open'', ''UnderInvestigation'', ''f44ba162-e62d-4f9e-9c25-000000000002''),
                    (''Open'', ''PendingPayment'', ''f44ba162-e62d-4f9e-9c25-000000000003''),
                    (''Open'', ''Closed'', ''f44ba162-e62d-4f9e-9c25-000000000004''),
                    (''Open'', ''Withdrawn'', ''f44ba162-e62d-4f9e-9c25-000000000005''),
                    (''UnderInvestigation'', ''Open'', ''f44ba162-e62d-4f9e-9c25-000000000006''),
                    (''UnderInvestigation'', ''PendingPayment'', ''f44ba162-e62d-4f9e-9c25-000000000007''),
                    (''UnderInvestigation'', ''Closed'', ''f44ba162-e62d-4f9e-9c25-000000000008''),
                    (''UnderInvestigation'', ''Withdrawn'', ''f44ba162-e62d-4f9e-9c25-000000000009''),
                    (''PendingPayment'', ''Closed'', ''f44ba162-e62d-4f9e-9c25-000000000010''),
                    (''Closed'', ''Reopened'', ''f44ba162-e62d-4f9e-9c25-000000000011''),
                    (''Reopened'', ''Open'', ''f44ba162-e62d-4f9e-9c25-000000000012'')
                ) AS seed([FromStatus], [ToStatus], [Id])
                    ON seed.[FromStatus] = transition.[FromStatus] AND seed.[ToStatus] = transition.[ToStatus];
            ');
            """);

        migrationBuilder.AlterColumn<Guid>("OrganizationEntityId", "ClaimStatusTransitions", "uniqueidentifier",
            nullable: false, oldClrType: typeof(Guid), oldType: "uniqueidentifier", oldNullable: true);
        migrationBuilder.AlterColumn<DateTimeOffset>("CreatedAt", "ClaimStatusTransitions", "datetimeoffset(7)",
            precision: 7, nullable: false, oldClrType: typeof(DateTimeOffset), oldType: "datetimeoffset(7)",
            oldPrecision: 7, oldNullable: true);
        migrationBuilder.DropPrimaryKey("PK_ClaimStatusTransitions", "ClaimStatusTransitions");
        migrationBuilder.AddPrimaryKey("PK_ClaimStatusTransitions", "ClaimStatusTransitions", "Id");
        migrationBuilder.CreateIndex("IX_ClaimStatusTransitions_OrganizationEntityId_FromStatus_ToStatus",
            "ClaimStatusTransitions", new[] { "OrganizationEntityId", "FromStatus", "ToStatus" },
            unique: true, filter: "[IsDeleted] = 0");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            IF EXISTS (
                SELECT 1 FROM [ClaimStatusTransitions]
                WHERE [OrganizationEntityId] <> '61fd4d23-97a2-4c4a-ace1-96a3b3e738d4' OR [IsDeleted] = 1
            )
                THROW 50003, 'Archived or tenant-specific transition rules cannot be represented in the previous schema.', 1;
            """);

        migrationBuilder.DropIndex("IX_ClaimStatusTransitions_OrganizationEntityId_FromStatus_ToStatus", "ClaimStatusTransitions");
        migrationBuilder.DropPrimaryKey("PK_ClaimStatusTransitions", "ClaimStatusTransitions");
        migrationBuilder.DropColumn("Id", "ClaimStatusTransitions");
        migrationBuilder.DropColumn("OrganizationEntityId", "ClaimStatusTransitions");
        migrationBuilder.DropColumn("CreatedAt", "ClaimStatusTransitions");
        migrationBuilder.DropColumn("CreatedByUserId", "ClaimStatusTransitions");
        migrationBuilder.DropColumn("UpdatedAt", "ClaimStatusTransitions");
        migrationBuilder.DropColumn("UpdatedByUserId", "ClaimStatusTransitions");
        migrationBuilder.DropColumn("IsDeleted", "ClaimStatusTransitions");
        migrationBuilder.DropColumn("DeletedAt", "ClaimStatusTransitions");
        migrationBuilder.AddPrimaryKey("PK_ClaimStatusTransitions", "ClaimStatusTransitions", new[] { "FromStatus", "ToStatus" });
    }
}
