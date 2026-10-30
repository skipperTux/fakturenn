using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Fakturenn.Modules.Catalog.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class WidenUnitPriceScale : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<decimal>(
                name: "UnitPrice",
                schema: "catalog",
                table: "CatalogItems",
                type: "numeric(20,6)",
                precision: 20,
                scale: 6,
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "numeric(18,4)",
                oldPrecision: 18,
                oldScale: 4);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<decimal>(
                name: "UnitPrice",
                schema: "catalog",
                table: "CatalogItems",
                type: "numeric(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "numeric(20,6)",
                oldPrecision: 20,
                oldScale: 6);
        }
    }
}
