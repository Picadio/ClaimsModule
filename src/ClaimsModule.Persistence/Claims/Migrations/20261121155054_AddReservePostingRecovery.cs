using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ClaimsModule.Persistence.Claims.Migrations
{
    /// <inheritdoc />
    public partial class AddReservePostingRecovery : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "PostingCorrelationId",
                table: "ReserveHistory",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_ReserveHistory_OrganizationEntityId_PostingStatus_Id",
                table: "ReserveHistory",
                columns: new[] { "OrganizationEntityId", "PostingStatus", "Id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ReserveHistory_OrganizationEntityId_PostingStatus_Id",
                table: "ReserveHistory");

            migrationBuilder.DropColumn(
                name: "PostingCorrelationId",
                table: "ReserveHistory");
        }
    }
}
