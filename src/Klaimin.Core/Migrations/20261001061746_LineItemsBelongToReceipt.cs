using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Klaimin.Core.Migrations
{
    /// <inheritdoc />
    public partial class LineItemsBelongToReceipt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_LineItem_Receipts_ReceiptId",
                table: "LineItem");

            migrationBuilder.AlterColumn<int>(
                name: "ReceiptId",
                table: "LineItem",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "INTEGER",
                oldNullable: true);

            migrationBuilder.AddForeignKey(
                name: "FK_LineItem_Receipts_ReceiptId",
                table: "LineItem",
                column: "ReceiptId",
                principalTable: "Receipts",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_LineItem_Receipts_ReceiptId",
                table: "LineItem");

            migrationBuilder.AlterColumn<int>(
                name: "ReceiptId",
                table: "LineItem",
                type: "INTEGER",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "INTEGER");

            migrationBuilder.AddForeignKey(
                name: "FK_LineItem_Receipts_ReceiptId",
                table: "LineItem",
                column: "ReceiptId",
                principalTable: "Receipts",
                principalColumn: "Id");
        }
    }
}
