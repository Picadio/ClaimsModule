using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ClaimsModule.Persistence.Claims.Migrations
{
    /// <inheritdoc />
    public partial class AddReserveDecisions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ApprovedByUserId",
                table: "ReserveHistory",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "RejectedAt",
                table: "ReserveHistory",
                type: "datetimeoffset(7)",
                precision: 7,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "RejectedByUserId",
                table: "ReserveHistory",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RejectionReason",
                table: "ReserveHistory",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ReplacesRejectedTransactionId",
                table: "ReserveHistory",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "ReserveLimitOverrideEnabled",
                table: "Claims",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddUniqueConstraint(
                name: "AK_ReserveHistory_OrganizationEntityId_ClaimId_ReserveId_Id",
                table: "ReserveHistory",
                columns: new[] { "OrganizationEntityId", "ClaimId", "ReserveId", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_ReserveHistory_OrganizationEntityId_ClaimId_ReserveId_ReplacesRejectedTransactionId",
                table: "ReserveHistory",
                columns: new[] { "OrganizationEntityId", "ClaimId", "ReserveId", "ReplacesRejectedTransactionId" });

            migrationBuilder.AddForeignKey(
                name: "FK_ReserveHistory_ReserveHistory_OrganizationEntityId_ClaimId_ReserveId_ReplacesRejectedTransactionId",
                table: "ReserveHistory",
                columns: new[] { "OrganizationEntityId", "ClaimId", "ReserveId", "ReplacesRejectedTransactionId" },
                principalTable: "ReserveHistory",
                principalColumns: new[] { "OrganizationEntityId", "ClaimId", "ReserveId", "Id" },
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ReserveHistory_ReserveHistory_OrganizationEntityId_ClaimId_ReserveId_ReplacesRejectedTransactionId",
                table: "ReserveHistory");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_ReserveHistory_OrganizationEntityId_ClaimId_ReserveId_Id",
                table: "ReserveHistory");

            migrationBuilder.DropIndex(
                name: "IX_ReserveHistory_OrganizationEntityId_ClaimId_ReserveId_ReplacesRejectedTransactionId",
                table: "ReserveHistory");

            migrationBuilder.DropColumn(
                name: "ApprovedByUserId",
                table: "ReserveHistory");

            migrationBuilder.DropColumn(
                name: "RejectedAt",
                table: "ReserveHistory");

            migrationBuilder.DropColumn(
                name: "RejectedByUserId",
                table: "ReserveHistory");

            migrationBuilder.DropColumn(
                name: "RejectionReason",
                table: "ReserveHistory");

            migrationBuilder.DropColumn(
                name: "ReplacesRejectedTransactionId",
                table: "ReserveHistory");

            migrationBuilder.DropColumn(
                name: "ReserveLimitOverrideEnabled",
                table: "Claims");
        }
    }
}
