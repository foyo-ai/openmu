using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MUnique.OpenMU.Persistence.EntityFramework.Migrations
{
    /// <inheritdoc />
    public partial class AddItemBank : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ItemBankEntry",
                schema: "data",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AccountId = table.Column<Guid>(type: "uuid", nullable: true),
                    ItemGroup = table.Column<byte>(type: "smallint", nullable: false),
                    ItemNumber = table.Column<short>(type: "smallint", nullable: false),
                    Count = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ItemBankEntry", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ItemBankEntry_Account_AccountId",
                        column: x => x.AccountId,
                        principalSchema: "data",
                        principalTable: "Account",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ItemBankEntry_AccountId",
                schema: "data",
                table: "ItemBankEntry",
                column: "AccountId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ItemBankEntry",
                schema: "data");
        }
    }
}
