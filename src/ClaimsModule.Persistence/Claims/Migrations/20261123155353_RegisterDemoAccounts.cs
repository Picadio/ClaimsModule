using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ClaimsModule.Persistence.Claims.Migrations
{
    public partial class RegisterDemoAccounts : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Preserve names and IDs referenced by claims, reserve history and audit entries.
            migrationBuilder.Sql("""
                UPDATE [dbo].[Users]
                SET [IsDeleted] = 1,
                    [DeletedAt] = COALESCE([DeletedAt], SYSUTCDATETIME()),
                    [PasswordHash] = '',
                    [NormalizedUserName] = CONCAT('RETIRED:', CONVERT(varchar(36), [Id]))
                WHERE [OrganizationEntityId] = '61fd4d23-97a2-4c4a-ace1-96a3b3e738d4'
                    AND [Id] IN ('76352da5-cc3f-49c0-8caf-149e6f51c4ec',
                                 'a257d183-69ee-494a-84c3-5ce2215a26ab',
                                 'c274bc9c-934e-4910-a759-f1afbef3e46f');
                """);

            migrationBuilder.AddColumn<string>(
                name: "RegistrationKeyHash",
                table: "Users",
                type: "varchar(64)",
                unicode: false,
                maxLength: 64,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Users_OrganizationEntityId_RegistrationKeyHash",
                table: "Users",
                columns: new[] { "OrganizationEntityId", "RegistrationKeyHash" },
                unique: true,
                filter: "[RegistrationKeyHash] IS NOT NULL");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                THROW 51000, 'Account retirement cannot be reverted by migration. Restore the pre-upgrade database backup to roll back.', 1;
                """);
        }
    }
}
