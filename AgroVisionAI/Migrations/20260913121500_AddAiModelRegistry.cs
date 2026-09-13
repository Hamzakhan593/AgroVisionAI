using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AgroVisionAI.Migrations
{
    /// <inheritdoc />
    public partial class AddAiModelRegistry : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AiModelRegistryEntries",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Crop = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Filename = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Version = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Architecture = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: true),
                    Accuracy = table.Column<double>(type: "float", nullable: true),
                    MacroF1 = table.Column<double>(type: "float", nullable: true),
                    ExternalAccuracy = table.Column<double>(type: "float", nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    IsAvailable = table.Column<bool>(type: "bit", nullable: false),
                    IsLoaded = table.Column<bool>(type: "bit", nullable: false),
                    SelectionLockedByEnvironment = table.Column<bool>(type: "bit", nullable: false),
                    Preprocessing = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    Notes = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    LastActivatedByUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    ActivatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LastSeenAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AiModelRegistryEntries", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AiModelRegistryEntries_Status",
                table: "AiModelRegistryEntries",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_AiModelRegistryEntries_Crop_Filename",
                table: "AiModelRegistryEntries",
                columns: new[] { "Crop", "Filename" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "AiModelRegistryEntries");
        }
    }
}
