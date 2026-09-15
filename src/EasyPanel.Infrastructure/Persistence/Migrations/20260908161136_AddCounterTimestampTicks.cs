using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EasyPanel.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCounterTimestampTicks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_PrinterCounters_TenantId_PrinterId_CounterType_Timestamp",
                table: "PrinterCounters");

            migrationBuilder.AddColumn<long>(
                name: "TimestampTicks",
                table: "PrinterCounters",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.CreateIndex(
                name: "IX_PrinterCounters_TenantId_PrinterId_CounterType_TimestampTic~",
                table: "PrinterCounters",
                columns: new[] { "TenantId", "PrinterId", "CounterType", "TimestampTicks" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_PrinterCounters_TenantId_PrinterId_CounterType_TimestampTic~",
                table: "PrinterCounters");

            migrationBuilder.DropColumn(
                name: "TimestampTicks",
                table: "PrinterCounters");

            migrationBuilder.CreateIndex(
                name: "IX_PrinterCounters_TenantId_PrinterId_CounterType_Timestamp",
                table: "PrinterCounters",
                columns: new[] { "TenantId", "PrinterId", "CounterType", "Timestamp" });
        }
    }
}
