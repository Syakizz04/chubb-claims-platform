using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Insights.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ClaimSnapshots",
                columns: table => new
                {
                    ClaimId = table.Column<Guid>(type: "uuid", nullable: false),
                    Reference = table.Column<string>(type: "text", nullable: false),
                    MarketCode = table.Column<string>(type: "text", nullable: false),
                    CurrencyCode = table.Column<string>(type: "text", nullable: false),
                    ClaimType = table.Column<string>(type: "text", nullable: false),
                    Status = table.Column<string>(type: "text", nullable: false),
                    ClaimantId = table.Column<string>(type: "text", nullable: false),
                    HandlerId = table.Column<string>(type: "text", nullable: true),
                    HandlerName = table.Column<string>(type: "text", nullable: true),
                    ClaimedAmount = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    ReserveAmount = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    ApprovedAmount = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: true),
                    SettledAmount = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: true),
                    SubmittedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    DecidedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ClosedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LastSequence = table.Column<int>(type: "integer", nullable: false),
                    LastEventAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClaimSnapshots", x => x.ClaimId);
                });

            migrationBuilder.CreateTable(
                name: "FxRates",
                columns: table => new
                {
                    CurrencyCode = table.Column<string>(type: "text", nullable: false),
                    UsdPerUnit = table.Column<decimal>(type: "numeric(19,10)", precision: 19, scale: 10, nullable: false),
                    AsOf = table.Column<DateOnly>(type: "date", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FxRates", x => x.CurrencyCode);
                });

            migrationBuilder.CreateTable(
                name: "Notifications",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EventId = table.Column<Guid>(type: "uuid", nullable: false),
                    ClaimId = table.Column<Guid>(type: "uuid", nullable: false),
                    ClaimantId = table.Column<string>(type: "text", nullable: false),
                    Reference = table.Column<string>(type: "text", nullable: false),
                    Message = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Notifications", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ProcessedEvents",
                columns: table => new
                {
                    EventId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProcessedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    OccurredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProcessedEvents", x => x.EventId);
                });

            migrationBuilder.InsertData(
                table: "FxRates",
                columns: new[] { "CurrencyCode", "AsOf", "UsdPerUnit" },
                values: new object[,]
                {
                    { "HKD", new DateOnly(2026, 11, 1), 0.1285m },
                    { "KRW", new DateOnly(2026, 11, 1), 0.00072m },
                    { "MYR", new DateOnly(2026, 11, 1), 0.2250m },
                    { "SGD", new DateOnly(2026, 11, 1), 0.7700m },
                    { "THB", new DateOnly(2026, 11, 1), 0.0290m },
                    { "TWD", new DateOnly(2026, 11, 1), 0.0310m },
                    { "USD", new DateOnly(2026, 11, 1), 1m }
                });

            migrationBuilder.CreateIndex(
                name: "IX_ClaimSnapshots_HandlerId",
                table: "ClaimSnapshots",
                column: "HandlerId");

            migrationBuilder.CreateIndex(
                name: "IX_ClaimSnapshots_Status_MarketCode",
                table: "ClaimSnapshots",
                columns: new[] { "Status", "MarketCode" });

            migrationBuilder.CreateIndex(
                name: "IX_Notifications_ClaimantId_CreatedAt",
                table: "Notifications",
                columns: new[] { "ClaimantId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Notifications_EventId",
                table: "Notifications",
                column: "EventId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ClaimSnapshots");

            migrationBuilder.DropTable(
                name: "FxRates");

            migrationBuilder.DropTable(
                name: "Notifications");

            migrationBuilder.DropTable(
                name: "ProcessedEvents");
        }
    }
}
