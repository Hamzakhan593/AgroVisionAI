using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AgroVisionAI.Migrations
{
    /// <inheritdoc />
    public partial class AddPredictionFeedbackLoop : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PredictionFeedbacks",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    DetectionId = table.Column<int>(type: "int", nullable: false),
                    UserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    Verdict = table.Column<int>(type: "int", nullable: false),
                    SuggestedDisease = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: true),
                    Comment = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    VerificationStatus = table.Column<int>(type: "int", nullable: false),
                    VerifiedDisease = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: true),
                    ReviewerNote = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    VerifiedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PredictionFeedbacks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PredictionFeedbacks_Detections_DetectionId",
                        column: x => x.DetectionId,
                        principalTable: "Detections",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PredictionFeedbacks_DetectionId",
                table: "PredictionFeedbacks",
                column: "DetectionId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PredictionFeedbacks_UserId",
                table: "PredictionFeedbacks",
                column: "UserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PredictionFeedbacks");
        }
    }
}
