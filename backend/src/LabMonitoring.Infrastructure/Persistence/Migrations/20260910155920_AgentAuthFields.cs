using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LabMonitoring.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AgentAuthFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AgentTokenHash",
                table: "Computers",
                type: "nvarchar(255)",
                maxLength: 255,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<bool>(
                name: "IsApproved",
                table: "Computers",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateIndex(
                name: "IX_Computers_AgentTokenHash",
                table: "Computers",
                column: "AgentTokenHash",
                unique: true,
                filter: "[AgentTokenHash] <> ''");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Computers_AgentTokenHash",
                table: "Computers");

            migrationBuilder.DropColumn(
                name: "AgentTokenHash",
                table: "Computers");

            migrationBuilder.DropColumn(
                name: "IsApproved",
                table: "Computers");
        }
    }
}
