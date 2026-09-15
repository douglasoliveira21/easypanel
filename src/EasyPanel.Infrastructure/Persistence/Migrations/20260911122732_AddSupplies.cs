using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EasyPanel.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSupplies : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SupplyReadings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PrinterId = table.Column<Guid>(type: "uuid", nullable: false),
                    Label = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Percent = table.Column<int>(type: "integer", nullable: false),
                    Timestamp = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    TimestampTicks = table.Column<long>(type: "bigint", nullable: false),
                    WindowsClientId = table.Column<Guid>(type: "uuid", nullable: true),
                    CollectionId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SupplyReadings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SupplyReadings_Printers_PrinterId",
                        column: x => x.PrinterId,
                        principalTable: "Printers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SupplyThresholds",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PrinterId = table.Column<Guid>(type: "uuid", nullable: true),
                    Label = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    ThresholdPercent = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SupplyThresholds", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SupplyThresholds_Printers_PrinterId",
                        column: x => x.PrinterId,
                        principalTable: "Printers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SupplyReadings_PrinterId",
                table: "SupplyReadings",
                column: "PrinterId");

            migrationBuilder.CreateIndex(
                name: "IX_SupplyReadings_TenantId",
                table: "SupplyReadings",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_SupplyReadings_TenantId_PrinterId_Label_TimestampTicks",
                table: "SupplyReadings",
                columns: new[] { "TenantId", "PrinterId", "Label", "TimestampTicks" });

            migrationBuilder.CreateIndex(
                name: "IX_SupplyThresholds_PrinterId",
                table: "SupplyThresholds",
                column: "PrinterId");

            migrationBuilder.CreateIndex(
                name: "IX_SupplyThresholds_TenantId",
                table: "SupplyThresholds",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_SupplyThresholds_TenantId_PrinterId_Label",
                table: "SupplyThresholds",
                columns: new[] { "TenantId", "PrinterId", "Label" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SupplyReadings");

            migrationBuilder.DropTable(
                name: "SupplyThresholds");
        }
    }
}
