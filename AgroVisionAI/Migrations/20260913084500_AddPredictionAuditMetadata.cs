using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AgroVisionAI.Migrations
{
    /// <inheritdoc />
    public partial class AddPredictionAuditMetadata : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<double>(
                name: "ConfidenceMargin",
                table: "Detections",
                type: "float",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "CropConfidence",
                table: "Detections",
                type: "float",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CropModelName",
                table: "Detections",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CropModelVersion",
                table: "Detections",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DiseaseModelName",
                table: "Detections",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DiseaseModelVersion",
                table: "Detections",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PredictedClass",
                table: "Detections",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "ProcessingTimeMs",
                table: "Detections",
                type: "float",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RequestId",
                table: "Detections",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "SecondConfidence",
                table: "Detections",
                type: "float",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SecondPredictedClass",
                table: "Detections",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SecondPredictionName",
                table: "Detections",
                type: "nvarchar(150)",
                maxLength: 150,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "ConfidenceMargin", table: "Detections");
            migrationBuilder.DropColumn(name: "CropConfidence", table: "Detections");
            migrationBuilder.DropColumn(name: "CropModelName", table: "Detections");
            migrationBuilder.DropColumn(name: "CropModelVersion", table: "Detections");
            migrationBuilder.DropColumn(name: "DiseaseModelName", table: "Detections");
            migrationBuilder.DropColumn(name: "DiseaseModelVersion", table: "Detections");
            migrationBuilder.DropColumn(name: "PredictedClass", table: "Detections");
            migrationBuilder.DropColumn(name: "ProcessingTimeMs", table: "Detections");
            migrationBuilder.DropColumn(name: "RequestId", table: "Detections");
            migrationBuilder.DropColumn(name: "SecondConfidence", table: "Detections");
            migrationBuilder.DropColumn(name: "SecondPredictedClass", table: "Detections");
            migrationBuilder.DropColumn(name: "SecondPredictionName", table: "Detections");
        }
    }
}
