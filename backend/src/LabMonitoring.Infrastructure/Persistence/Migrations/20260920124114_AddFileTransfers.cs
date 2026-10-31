using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LabMonitoring.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddFileTransfers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "FileTransfers",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Direction = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    CommandId = table.Column<long>(type: "bigint", nullable: true),
                    RoomId = table.Column<int>(type: "int", nullable: true),
                    ComputerId = table.Column<int>(type: "int", nullable: true),
                    TransferFolderId = table.Column<int>(type: "int", nullable: true),
                    FolderPath = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    FileName = table.Column<string>(type: "nvarchar(260)", maxLength: 260, nullable: true),
                    SizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    Sha256 = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    Overwrite = table.Column<bool>(type: "bit", nullable: false),
                    Filter = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    MaxBytes = table.Column<long>(type: "bigint", nullable: false),
                    CreatedById = table.Column<int>(type: "int", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FileTransfers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FileTransfers_Users_CreatedById",
                        column: x => x.CreatedById,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "TransferFolders",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    RoomId = table.Column<int>(type: "int", nullable: true),
                    Path = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    Direction = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    Label = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TransferFolders", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TransferFolders_Rooms_RoomId",
                        column: x => x.RoomId,
                        principalTable: "Rooms",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "CollectedFiles",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    FileTransferId = table.Column<int>(type: "int", nullable: false),
                    ComputerId = table.Column<int>(type: "int", nullable: false),
                    SizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    Sha256 = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    FileCount = table.Column<int>(type: "int", nullable: false),
                    Truncated = table.Column<bool>(type: "bit", nullable: false),
                    ReceivedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CollectedFiles", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CollectedFiles_Computers_ComputerId",
                        column: x => x.ComputerId,
                        principalTable: "Computers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_CollectedFiles_FileTransfers_FileTransferId",
                        column: x => x.FileTransferId,
                        principalTable: "FileTransfers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CollectedFiles_ComputerId",
                table: "CollectedFiles",
                column: "ComputerId");

            migrationBuilder.CreateIndex(
                name: "IX_CollectedFiles_FileTransferId_ComputerId",
                table: "CollectedFiles",
                columns: new[] { "FileTransferId", "ComputerId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FileTransfers_CommandId",
                table: "FileTransfers",
                column: "CommandId");

            migrationBuilder.CreateIndex(
                name: "IX_FileTransfers_CreatedAt",
                table: "FileTransfers",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_FileTransfers_CreatedById",
                table: "FileTransfers",
                column: "CreatedById");

            migrationBuilder.CreateIndex(
                name: "IX_FileTransfers_RoomId",
                table: "FileTransfers",
                column: "RoomId");

            migrationBuilder.CreateIndex(
                name: "IX_TransferFolders_RoomId_Direction",
                table: "TransferFolders",
                columns: new[] { "RoomId", "Direction" });

            // New top-level "Truyền file" screen (SeededScreens.FileTransferId). Visible to every console role;
            // managing the allowed-folder list is Administrator-only at the endpoint level.
            migrationBuilder.InsertData(
                table: "FunctionScreens",
                columns: new[] { "Id", "Code", "CreatedAt", "DisplayOrder", "Icon", "IsActive", "Name", "ParentId", "Route" },
                values: new object[] { 13, "file-transfer", new DateTime(2026, 9, 20, 0, 0, 0, 0, DateTimeKind.Utc), 5, "bi-arrow-left-right", true, "Truyền file", null, "/file-transfer" });

            migrationBuilder.InsertData(
                table: "RoleScreenPermissions",
                columns: new[] { "RoleId", "ScreenId" },
                values: new object[,] { { 1, 13 }, { 2, 13 }, { 3, 13 } });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "RoleScreenPermissions",
                keyColumns: new[] { "RoleId", "ScreenId" },
                keyValues: new object[] { 1, 13 });
            migrationBuilder.DeleteData(
                table: "RoleScreenPermissions",
                keyColumns: new[] { "RoleId", "ScreenId" },
                keyValues: new object[] { 2, 13 });
            migrationBuilder.DeleteData(
                table: "RoleScreenPermissions",
                keyColumns: new[] { "RoleId", "ScreenId" },
                keyValues: new object[] { 3, 13 });
            migrationBuilder.DeleteData(
                table: "FunctionScreens",
                keyColumn: "Id",
                keyValue: 13);

            migrationBuilder.DropTable(
                name: "CollectedFiles");

            migrationBuilder.DropTable(
                name: "TransferFolders");

            migrationBuilder.DropTable(
                name: "FileTransfers");
        }
    }
}
