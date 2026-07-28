using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EnglishMasterAI.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class EngagementObservabilityAndReviews : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<double>(
                name: "AccuracyScore",
                table: "SpeakingSubmissions",
                type: "REAL",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "CompletenessScore",
                table: "SpeakingSubmissions",
                type: "REAL",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PronunciationProvider",
                table: "SpeakingSubmissions",
                type: "TEXT",
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "PronunciationScore",
                table: "SpeakingSubmissions",
                type: "REAL",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "ProsodyScore",
                table: "SpeakingSubmissions",
                type: "REAL",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TimeZoneId",
                table: "LearnerProfiles",
                type: "TEXT",
                maxLength: 80,
                nullable: false,
                defaultValue: "Asia/Bangkok");

            migrationBuilder.CreateTable(
                name: "AiUsageRecords",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    UserId = table.Column<string>(type: "TEXT", maxLength: 450, nullable: false),
                    Operation = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    Provider = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    Model = table.Column<string>(type: "TEXT", maxLength: 80, nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    InputUnits = table.Column<int>(type: "INTEGER", nullable: false),
                    InputTokens = table.Column<int>(type: "INTEGER", nullable: false),
                    OutputTokens = table.Column<int>(type: "INTEGER", nullable: false),
                    DurationMilliseconds = table.Column<int>(type: "INTEGER", nullable: false),
                    RequestId = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    FailureType = table.Column<string>(type: "TEXT", maxLength: 120, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AiUsageRecords", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ContentReviewAssignments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    LessonId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ReviewerUserId = table.Column<string>(type: "TEXT", maxLength: 450, nullable: true),
                    ReviewerRole = table.Column<string>(type: "TEXT", maxLength: 80, nullable: false),
                    Status = table.Column<string>(type: "TEXT", nullable: false),
                    Notes = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: false),
                    AssignedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    ReviewedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ContentReviewAssignments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ContentReviewAssignments_Lessons_LessonId",
                        column: x => x.LessonId,
                        principalTable: "Lessons",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "LearnerAchievements",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    UserId = table.Column<string>(type: "TEXT", maxLength: 450, nullable: false),
                    Code = table.Column<string>(type: "TEXT", maxLength: 60, nullable: false),
                    Title = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false),
                    Description = table.Column<string>(type: "TEXT", maxLength: 300, nullable: false),
                    EarnedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LearnerAchievements", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "LearningActivities",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    UserId = table.Column<string>(type: "TEXT", maxLength: 450, nullable: false),
                    Kind = table.Column<string>(type: "TEXT", maxLength: 60, nullable: false),
                    DeduplicationKey = table.Column<string>(type: "TEXT", maxLength: 160, nullable: false),
                    Minutes = table.Column<int>(type: "INTEGER", nullable: false),
                    Points = table.Column<int>(type: "INTEGER", nullable: false),
                    OccurredAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LearningActivities", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AiUsageRecords_CreatedAt",
                table: "AiUsageRecords",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_ContentReviewAssignments_LessonId_ReviewerRole",
                table: "ContentReviewAssignments",
                columns: new[] { "LessonId", "ReviewerRole" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LearnerAchievements_UserId_Code",
                table: "LearnerAchievements",
                columns: new[] { "UserId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LearningActivities_UserId_DeduplicationKey",
                table: "LearningActivities",
                columns: new[] { "UserId", "DeduplicationKey" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AiUsageRecords");

            migrationBuilder.DropTable(
                name: "ContentReviewAssignments");

            migrationBuilder.DropTable(
                name: "LearnerAchievements");

            migrationBuilder.DropTable(
                name: "LearningActivities");

            migrationBuilder.DropColumn(
                name: "AccuracyScore",
                table: "SpeakingSubmissions");

            migrationBuilder.DropColumn(
                name: "CompletenessScore",
                table: "SpeakingSubmissions");

            migrationBuilder.DropColumn(
                name: "PronunciationProvider",
                table: "SpeakingSubmissions");

            migrationBuilder.DropColumn(
                name: "PronunciationScore",
                table: "SpeakingSubmissions");

            migrationBuilder.DropColumn(
                name: "ProsodyScore",
                table: "SpeakingSubmissions");

            migrationBuilder.DropColumn(
                name: "TimeZoneId",
                table: "LearnerProfiles");
        }
    }
}
