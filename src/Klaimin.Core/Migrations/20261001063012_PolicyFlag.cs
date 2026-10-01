using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Klaimin.Core.Migrations
{
    /// <inheritdoc />
    public partial class PolicyFlag : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "ExceededCap",
                table: "Receipts",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Justification",
                table: "Receipts",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ExceededCap",
                table: "Receipts");

            migrationBuilder.DropColumn(
                name: "Justification",
                table: "Receipts");
        }
    }
}
