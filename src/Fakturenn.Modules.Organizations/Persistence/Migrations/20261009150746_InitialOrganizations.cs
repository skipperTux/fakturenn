using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Fakturenn.Modules.Organizations.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialOrganizations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "organizations");

            migrationBuilder.CreateTable(
                name: "Organizations",
                schema: "organizations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    LegalName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    CountryCode = table.Column<string>(type: "character varying(2)", maxLength: 2, nullable: false),
                    VatId = table.Column<string>(type: "character varying(14)", maxLength: 14, nullable: false),
                    Iban = table.Column<string>(type: "character varying(34)", maxLength: 34, nullable: false),
                    Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    TimeZoneId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    PaymentTermsDays = table.Column<int>(type: "integer", nullable: false),
                    IssueDateDefault = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    NumberPrefix = table.Column<string>(type: "character varying(9)", maxLength: 9, nullable: false),
                    NumberScope = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    NumberPadding = table.Column<int>(type: "integer", nullable: false),
                    NumberNextValue = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    ModifiedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ModifiedBy = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Organizations", x => x.Id);
                    table.CheckConstraint("CK_Organizations_Singleton", "\"Id\" = '00000000-0000-0000-0000-000000000001'");
                });

            migrationBuilder.InsertData(
                schema: "organizations",
                table: "Organizations",
                columns: new[] { "Id", "CountryCode", "CreatedAt", "CreatedBy", "Currency", "Iban", "IssueDateDefault", "LegalName", "ModifiedAt", "ModifiedBy", "NumberNextValue", "NumberPadding", "NumberPrefix", "NumberScope", "PaymentTermsDays", "TimeZoneId", "VatId" },
                values: new object[] { new Guid("00000000-0000-0000-0000-000000000001"), "", new DateTimeOffset(new DateTime(2026, 10, 9, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "system", "", "", "Today", "", new DateTimeOffset(new DateTime(2026, 10, 9, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "system", 1L, 4, "", "PerYear", 14, "Europe/Berlin", "" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Organizations",
                schema: "organizations");
        }
    }
}
