using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EnglishMasterAI.Migrations.PostgreSql.Migrations
{
    /// <inheritdoc />
    public partial class LegalConsentEvidence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "PrivacyNoticeAcknowledgedAt",
                table: "AspNetUsers",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PrivacyNoticeVersion",
                table: "AspNetUsers",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "TermsAcceptedAt",
                table: "AspNetUsers",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TermsVersion",
                table: "AspNetUsers",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PrivacyNoticeAcknowledgedAt",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "PrivacyNoticeVersion",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "TermsAcceptedAt",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "TermsVersion",
                table: "AspNetUsers");
        }
    }
}
