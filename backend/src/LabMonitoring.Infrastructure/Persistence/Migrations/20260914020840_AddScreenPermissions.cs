using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace LabMonitoring.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddScreenPermissions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsSystem",
                table: "Roles",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "FunctionScreens",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Code = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    Route = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Icon = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    DisplayOrder = table.Column<int>(type: "int", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "SYSUTCDATETIME()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FunctionScreens", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "RoleScreenPermissions",
                columns: table => new
                {
                    RoleId = table.Column<int>(type: "int", nullable: false),
                    ScreenId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RoleScreenPermissions", x => new { x.RoleId, x.ScreenId });
                    table.ForeignKey(
                        name: "FK_RoleScreenPermissions_FunctionScreens_ScreenId",
                        column: x => x.ScreenId,
                        principalTable: "FunctionScreens",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_RoleScreenPermissions_Roles_RoleId",
                        column: x => x.RoleId,
                        principalTable: "Roles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "UserScreenPermissions",
                columns: table => new
                {
                    UserId = table.Column<int>(type: "int", nullable: false),
                    ScreenId = table.Column<int>(type: "int", nullable: false),
                    IsGranted = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserScreenPermissions", x => new { x.UserId, x.ScreenId });
                    table.ForeignKey(
                        name: "FK_UserScreenPermissions_FunctionScreens_ScreenId",
                        column: x => x.ScreenId,
                        principalTable: "FunctionScreens",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_UserScreenPermissions_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.InsertData(
                table: "FunctionScreens",
                columns: new[] { "Id", "Code", "CreatedAt", "DisplayOrder", "Icon", "IsActive", "Name", "Route" },
                values: new object[,]
                {
                    { 1, "dashboard", new DateTime(2026, 9, 14, 0, 0, 0, 0, DateTimeKind.Utc), 1, "bi-speedometer2", true, "Bảng điều khiển", "/dashboard" },
                    { 2, "rooms", new DateTime(2026, 9, 14, 0, 0, 0, 0, DateTimeKind.Utc), 2, "bi-hdd-network", true, "Phòng máy", "/rooms" },
                    { 3, "classes", new DateTime(2026, 9, 14, 0, 0, 0, 0, DateTimeKind.Utc), 3, "bi-mortarboard", true, "Lớp học", "/classes" },
                    { 4, "sessions", new DateTime(2026, 9, 14, 0, 0, 0, 0, DateTimeKind.Utc), 4, "bi-calendar-week", true, "Ca thực hành", "/sessions" },
                    { 5, "catalog", new DateTime(2026, 9, 14, 0, 0, 0, 0, DateTimeKind.Utc), 5, "bi-gear", true, "Danh mục", "/catalog" },
                    { 6, "reports.usage", new DateTime(2026, 9, 14, 0, 0, 0, 0, DateTimeKind.Utc), 6, "bi-bar-chart", true, "Báo cáo", "/reports" },
                    { 7, "users", new DateTime(2026, 9, 14, 0, 0, 0, 0, DateTimeKind.Utc), 7, "bi-people", true, "Người dùng", "/users" },
                    { 8, "roles", new DateTime(2026, 9, 14, 0, 0, 0, 0, DateTimeKind.Utc), 8, "bi-person-badge", true, "Vai trò", "/roles" },
                    { 9, "screens", new DateTime(2026, 9, 14, 0, 0, 0, 0, DateTimeKind.Utc), 9, "bi-window", true, "Màn hình chức năng", "/screens" },
                    { 10, "audit-logs", new DateTime(2026, 9, 14, 0, 0, 0, 0, DateTimeKind.Utc), 10, "bi-journal-text", true, "Nhật ký", "/audit-logs" }
                });

            migrationBuilder.UpdateData(
                table: "Roles",
                keyColumn: "Id",
                keyValue: 1,
                column: "IsSystem",
                value: true);

            migrationBuilder.UpdateData(
                table: "Roles",
                keyColumn: "Id",
                keyValue: 2,
                column: "IsSystem",
                value: true);

            migrationBuilder.UpdateData(
                table: "Roles",
                keyColumn: "Id",
                keyValue: 3,
                column: "IsSystem",
                value: true);

            migrationBuilder.InsertData(
                table: "RoleScreenPermissions",
                columns: new[] { "RoleId", "ScreenId" },
                values: new object[,]
                {
                    { 1, 1 },
                    { 1, 2 },
                    { 1, 3 },
                    { 1, 4 },
                    { 1, 5 },
                    { 1, 6 },
                    { 1, 7 },
                    { 1, 8 },
                    { 1, 9 },
                    { 1, 10 },
                    { 2, 1 },
                    { 2, 2 },
                    { 2, 3 },
                    { 2, 4 },
                    { 2, 6 },
                    { 3, 1 },
                    { 3, 2 },
                    { 3, 3 },
                    { 3, 4 },
                    { 3, 6 }
                });

            migrationBuilder.CreateIndex(
                name: "IX_FunctionScreens_Code",
                table: "FunctionScreens",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RoleScreenPermissions_ScreenId",
                table: "RoleScreenPermissions",
                column: "ScreenId");

            migrationBuilder.CreateIndex(
                name: "IX_UserScreenPermissions_ScreenId",
                table: "UserScreenPermissions",
                column: "ScreenId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RoleScreenPermissions");

            migrationBuilder.DropTable(
                name: "UserScreenPermissions");

            migrationBuilder.DropTable(
                name: "FunctionScreens");

            migrationBuilder.DropColumn(
                name: "IsSystem",
                table: "Roles");
        }
    }
}
