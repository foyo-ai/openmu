using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MUnique.OpenMU.Persistence.EntityFramework.Migrations
{
    /// <inheritdoc />
    public partial class AddStoreCurrency : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<byte>(
                name: "StoreCurrencyItemGroup",
                schema: "data",
                table: "Character",
                type: "smallint",
                nullable: true);

            migrationBuilder.AddColumn<short>(
                name: "StoreCurrencyItemNumber",
                schema: "data",
                table: "Character",
                type: "smallint",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "StoreCurrencyItemGroup",
                schema: "data",
                table: "Character");

            migrationBuilder.DropColumn(
                name: "StoreCurrencyItemNumber",
                schema: "data",
                table: "Character");
        }
    }
}
