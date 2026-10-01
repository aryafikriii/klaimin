using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Klaimin.Core.Migrations
{
    /// <inheritdoc />
    public partial class DuplicateFlag : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "DuplicateOfId",
                table: "Receipts",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ImageHash",
                table: "Receipts",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Receipts_DuplicateOfId",
                table: "Receipts",
                column: "DuplicateOfId");

            migrationBuilder.CreateIndex(
                name: "IX_Receipts_ImageHash",
                table: "Receipts",
                column: "ImageHash");

            migrationBuilder.AddForeignKey(
                name: "FK_Receipts_Receipts_DuplicateOfId",
                table: "Receipts",
                column: "DuplicateOfId",
                principalTable: "Receipts",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Receipts_Receipts_DuplicateOfId",
                table: "Receipts");

            migrationBuilder.DropIndex(
                name: "IX_Receipts_DuplicateOfId",
                table: "Receipts");

            migrationBuilder.DropIndex(
                name: "IX_Receipts_ImageHash",
                table: "Receipts");

            migrationBuilder.DropColumn(
                name: "DuplicateOfId",
                table: "Receipts");

            migrationBuilder.DropColumn(
                name: "ImageHash",
                table: "Receipts");
        }
    }
}
