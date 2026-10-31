using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LabMonitoring.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddResourceAlertTracking : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "CpuAlertRaisedAt",
                table: "ComputerStatuses",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "RamAlertRaisedAt",
                table: "ComputerStatuses",
                type: "datetime2",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CpuAlertRaisedAt",
                table: "ComputerStatuses");

            migrationBuilder.DropColumn(
                name: "RamAlertRaisedAt",
                table: "ComputerStatuses");
        }
    }
}
