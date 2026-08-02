using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EnglishMasterAI.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class DropVocabularyIsCritical : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsCritical",
                table: "VocabularyItems");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsCritical",
                table: "VocabularyItems",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);
        }
    }
}
