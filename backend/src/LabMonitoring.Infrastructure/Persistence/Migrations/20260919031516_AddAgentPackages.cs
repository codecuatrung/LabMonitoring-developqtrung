using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LabMonitoring.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAgentPackages : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AgentPackages",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Version = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    FileName = table.Column<string>(type: "nvarchar(260)", maxLength: 260, nullable: false),
                    FileSizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    Sha256 = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    UploadedById = table.Column<int>(type: "int", nullable: true),
                    UploadedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AgentPackages", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AgentPackages_Users_UploadedById",
                        column: x => x.UploadedById,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AgentPackages_UploadedAt",
                table: "AgentPackages",
                column: "UploadedAt");

            migrationBuilder.CreateIndex(
                name: "IX_AgentPackages_UploadedById",
                table: "AgentPackages",
                column: "UploadedById");

            migrationBuilder.CreateIndex(
                name: "IX_AgentPackages_Version",
                table: "AgentPackages",
                column: "Version",
                unique: true);

            // New "Phiên bản Agent" screen (SeededScreens.AgentUpdatesId), nested under "Hệ thống"
            // (Id 11) alongside Users/Roles/Screens (DisplayOrder 1-3) — mirrors AddScreenHierarchy.
            migrationBuilder.InsertData(
                table: "FunctionScreens",
                columns: new[] { "Id", "Code", "CreatedAt", "DisplayOrder", "Icon", "IsActive", "Name", "ParentId", "Route" },
                values: new object[] { 12, "agent-updates", new DateTime(2026, 9, 19, 0, 0, 0, 0, DateTimeKind.Utc), 4, "bi-cloud-arrow-up", true, "Phiên bản Agent", 11, "/agent-updates" });

            migrationBuilder.InsertData(
                table: "RoleScreenPermissions",
                columns: new[] { "RoleId", "ScreenId" },
                values: new object[] { 1, 12 });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "RoleScreenPermissions",
                keyColumns: new[] { "RoleId", "ScreenId" },
                keyValues: new object[] { 1, 12 });

            migrationBuilder.DeleteData(
                table: "FunctionScreens",
                keyColumn: "Id",
                keyValue: 12);

            migrationBuilder.DropTable(
                name: "AgentPackages");
        }
    }
}
