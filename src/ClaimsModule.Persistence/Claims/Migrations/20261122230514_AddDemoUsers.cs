using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace ClaimsModule.Persistence.Claims.Migrations
{
    /// <inheritdoc />
    public partial class AddDemoUsers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Users",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWSEQUENTIALID()"),
                    UserName = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    NormalizedUserName = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    DisplayName = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    Role = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    PasswordHash = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: false),
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
                    table.PrimaryKey("PK_Users", x => x.Id);
                });

            migrationBuilder.InsertData(
                table: "Users",
                columns: new[] { "Id", "CreatedAt", "CreatedByUserId", "DeletedAt", "DisplayName", "NormalizedUserName", "OrganizationEntityId", "PasswordHash", "Role", "UpdatedAt", "UpdatedByUserId", "UserName" },
                values: new object[,]
                {
                    { new Guid("76352da5-cc3f-49c0-8caf-149e6f51c4ec"), new DateTimeOffset(new DateTime(2026, 5, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, null, "handler", "HANDLER", new Guid("61fd4d23-97a2-4c4a-ace1-96a3b3e738d4"), "AQAAAAIAAYagAAAAEPCPZSt0N3U3KaXk74vwFlf7wGlpXgt0mGlfeHrU80OtvfkYt0Lhl7Z9h1v6qQuS9g==", "handler", null, null, "handler" },
                    { new Guid("a257d183-69ee-494a-84c3-5ce2215a26ab"), new DateTimeOffset(new DateTime(2026, 5, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, null, "manager", "MANAGER", new Guid("61fd4d23-97a2-4c4a-ace1-96a3b3e738d4"), "AQAAAAIAAYagAAAAENmVffbF5Cln+lQtKt8pQgBtYk9YPDiM0/fJpLg2miO8rFZ28TUmByixPKk6krJimw==", "manager", null, null, "manager" },
                    { new Guid("c274bc9c-934e-4910-a759-f1afbef3e46f"), new DateTimeOffset(new DateTime(2026, 5, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, null, "supervisor", "SUPERVISOR", new Guid("61fd4d23-97a2-4c4a-ace1-96a3b3e738d4"), "AQAAAAIAAYagAAAAEOtsbROcow/shg2MTZj00pcr78rJaL3kgwEl2VGkC2TwZi96KGTnJdr2nyVO1Fwxfw==", "supervisor", null, null, "supervisor" }
                });

            migrationBuilder.CreateIndex(
                name: "IX_Users_OrganizationEntityId_NormalizedUserName",
                table: "Users",
                columns: new[] { "OrganizationEntityId", "NormalizedUserName" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Users");
        }
    }
}
