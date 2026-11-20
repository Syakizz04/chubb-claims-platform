using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Claims.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class MarketTimeZone : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "TimeZoneId",
                table: "Markets",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "");

            // Backfill markets seeded before this column existed. A fresh database gets the values
            // from the seeder instead (the table is empty when this runs).
            migrationBuilder.Sql("""
                UPDATE "Markets" SET "TimeZoneId" = CASE "Code"
                    WHEN 'KR' THEN 'Asia/Seoul'
                    WHEN 'HK' THEN 'Asia/Hong_Kong'
                    WHEN 'TW' THEN 'Asia/Taipei'
                    WHEN 'TH' THEN 'Asia/Bangkok'
                    WHEN 'MY' THEN 'Asia/Kuala_Lumpur'
                    WHEN 'SG' THEN 'Asia/Singapore'
                    ELSE 'UTC' END;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "TimeZoneId",
                table: "Markets");
        }
    }
}
