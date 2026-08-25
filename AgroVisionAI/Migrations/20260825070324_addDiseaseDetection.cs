using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AgroVisionAI.Migrations
{
    /// <inheritdoc />
    public partial class addDiseaseDetection : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Disease",
                table: "Detections");

            migrationBuilder.AddColumn<int>(
                name: "DiseaseId",
                table: "Detections",
                type: "int",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Diseases",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Crop = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    Symptoms = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    Treatment = table.Column<string>(type: "nvarchar(3000)", maxLength: 3000, nullable: false),
                    Prevention = table.Column<string>(type: "nvarchar(3000)", maxLength: 3000, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Diseases", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Detections_DiseaseId",
                table: "Detections",
                column: "DiseaseId");

            migrationBuilder.AddForeignKey(
                name: "FK_Detections_Diseases_DiseaseId",
                table: "Detections",
                column: "DiseaseId",
                principalTable: "Diseases",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Detections_Diseases_DiseaseId",
                table: "Detections");

            migrationBuilder.DropTable(
                name: "Diseases");

            migrationBuilder.DropIndex(
                name: "IX_Detections_DiseaseId",
                table: "Detections");

            migrationBuilder.DropColumn(
                name: "DiseaseId",
                table: "Detections");

            migrationBuilder.AddColumn<string>(
                name: "Disease",
                table: "Detections",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);
        }
    }
}
