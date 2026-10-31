using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LabMonitoring.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddScreenHierarchy : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "Route",
                table: "FunctionScreens",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(200)",
                oldMaxLength: 200);

            migrationBuilder.AddColumn<int>(
                name: "ParentId",
                table: "FunctionScreens",
                type: "int",
                nullable: true);

            migrationBuilder.UpdateData(
                table: "FunctionScreens",
                keyColumn: "Id",
                keyValue: 1,
                column: "ParentId",
                value: null);

            migrationBuilder.UpdateData(
                table: "FunctionScreens",
                keyColumn: "Id",
                keyValue: 2,
                column: "ParentId",
                value: null);

            migrationBuilder.UpdateData(
                table: "FunctionScreens",
                keyColumn: "Id",
                keyValue: 3,
                column: "ParentId",
                value: null);

            migrationBuilder.UpdateData(
                table: "FunctionScreens",
                keyColumn: "Id",
                keyValue: 4,
                column: "ParentId",
                value: null);

            migrationBuilder.UpdateData(
                table: "FunctionScreens",
                keyColumn: "Id",
                keyValue: 5,
                column: "ParentId",
                value: null);

            migrationBuilder.UpdateData(
                table: "FunctionScreens",
                keyColumn: "Id",
                keyValue: 6,
                column: "ParentId",
                value: null);

            migrationBuilder.UpdateData(
                table: "FunctionScreens",
                keyColumn: "Id",
                keyValue: 7,
                columns: new[] { "DisplayOrder", "ParentId" },
                values: new object[] { 1, 11 });

            migrationBuilder.UpdateData(
                table: "FunctionScreens",
                keyColumn: "Id",
                keyValue: 8,
                columns: new[] { "DisplayOrder", "ParentId" },
                values: new object[] { 2, 11 });

            migrationBuilder.UpdateData(
                table: "FunctionScreens",
                keyColumn: "Id",
                keyValue: 9,
                columns: new[] { "DisplayOrder", "ParentId" },
                values: new object[] { 3, 11 });

            migrationBuilder.UpdateData(
                table: "FunctionScreens",
                keyColumn: "Id",
                keyValue: 10,
                columns: new[] { "DisplayOrder", "ParentId" },
                values: new object[] { 8, null });

            migrationBuilder.InsertData(
                table: "FunctionScreens",
                columns: new[] { "Id", "Code", "CreatedAt", "DisplayOrder", "Icon", "IsActive", "Name", "ParentId", "Route" },
                values: new object[] { 11, "system", new DateTime(2026, 9, 14, 0, 0, 0, 0, DateTimeKind.Utc), 7, "bi-diagram-3", true, "Hệ thống", null, null });

            migrationBuilder.InsertData(
                table: "RoleScreenPermissions",
                columns: new[] { "RoleId", "ScreenId" },
                values: new object[] { 1, 11 });

            migrationBuilder.CreateIndex(
                name: "IX_FunctionScreens_ParentId",
                table: "FunctionScreens",
                column: "ParentId");

            migrationBuilder.AddForeignKey(
                name: "FK_FunctionScreens_FunctionScreens_ParentId",
                table: "FunctionScreens",
                column: "ParentId",
                principalTable: "FunctionScreens",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_FunctionScreens_FunctionScreens_ParentId",
                table: "FunctionScreens");

            migrationBuilder.DropIndex(
                name: "IX_FunctionScreens_ParentId",
                table: "FunctionScreens");

            migrationBuilder.DeleteData(
                table: "RoleScreenPermissions",
                keyColumns: new[] { "RoleId", "ScreenId" },
                keyValues: new object[] { 1, 11 });

            migrationBuilder.DeleteData(
                table: "FunctionScreens",
                keyColumn: "Id",
                keyValue: 11);

            migrationBuilder.DropColumn(
                name: "ParentId",
                table: "FunctionScreens");

            migrationBuilder.AlterColumn<string>(
                name: "Route",
                table: "FunctionScreens",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "nvarchar(200)",
                oldMaxLength: 200,
                oldNullable: true);

            migrationBuilder.UpdateData(
                table: "FunctionScreens",
                keyColumn: "Id",
                keyValue: 7,
                column: "DisplayOrder",
                value: 7);

            migrationBuilder.UpdateData(
                table: "FunctionScreens",
                keyColumn: "Id",
                keyValue: 8,
                column: "DisplayOrder",
                value: 8);

            migrationBuilder.UpdateData(
                table: "FunctionScreens",
                keyColumn: "Id",
                keyValue: 9,
                column: "DisplayOrder",
                value: 9);

            migrationBuilder.UpdateData(
                table: "FunctionScreens",
                keyColumn: "Id",
                keyValue: 10,
                column: "DisplayOrder",
                value: 10);
        }
    }
}
