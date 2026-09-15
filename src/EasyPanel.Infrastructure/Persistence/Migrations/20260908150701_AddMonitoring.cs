using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EasyPanel.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddMonitoring : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PrinterEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PrinterId = table.Column<Guid>(type: "uuid", nullable: true),
                    WindowsClientId = table.Column<Guid>(type: "uuid", nullable: true),
                    Type = table.Column<int>(type: "integer", nullable: false),
                    Detail = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    OccurredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PrinterEvents", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Printers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CustomerId = table.Column<Guid>(type: "uuid", nullable: false),
                    LocationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Fabricante = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    Modelo = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    NumeroSerie = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    Patrimonio = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    Ip = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    Mac = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    Hostname = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    Protocolo = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    Porta = table.Column<int>(type: "integer", nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    MonitoringEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    InstalledAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Observacoes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Printers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Printers_Customers_CustomerId",
                        column: x => x.CustomerId,
                        principalTable: "Customers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Printers_Locations_LocationId",
                        column: x => x.LocationId,
                        principalTable: "Locations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "WindowsClients",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CustomerId = table.Column<Guid>(type: "uuid", nullable: false),
                    LocationId = table.Column<Guid>(type: "uuid", nullable: false),
                    UniqueId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Hostname = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    AgentVersion = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    State = table.Column<int>(type: "integer", nullable: false),
                    LastHeartbeatAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LastCollectionAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    SecretHash = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WindowsClients", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WindowsClients_Customers_CustomerId",
                        column: x => x.CustomerId,
                        principalTable: "Customers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_WindowsClients_Locations_LocationId",
                        column: x => x.LocationId,
                        principalTable: "Locations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PrinterCounters",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PrinterId = table.Column<Guid>(type: "uuid", nullable: false),
                    Timestamp = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CounterType = table.Column<int>(type: "integer", nullable: false),
                    CounterTypeLabel = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    Value = table.Column<long>(type: "bigint", nullable: false),
                    Source = table.Column<int>(type: "integer", nullable: false),
                    WindowsClientId = table.Column<Guid>(type: "uuid", nullable: true),
                    CollectionId = table.Column<Guid>(type: "uuid", nullable: true),
                    IsAdministrativeAdjustment = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PrinterCounters", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PrinterCounters_Printers_PrinterId",
                        column: x => x.PrinterId,
                        principalTable: "Printers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PrinterMovements",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PrinterId = table.Column<Guid>(type: "uuid", nullable: false),
                    Operation = table.Column<int>(type: "integer", nullable: false),
                    FromLocationId = table.Column<Guid>(type: "uuid", nullable: true),
                    ToLocationId = table.Column<Guid>(type: "uuid", nullable: true),
                    ActorUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    OccurredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PrinterMovements", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PrinterMovements_Printers_PrinterId",
                        column: x => x.PrinterId,
                        principalTable: "Printers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Collections",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    IdempotencyKey = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    WindowsClientId = table.Column<Guid>(type: "uuid", nullable: false),
                    PrinterId = table.Column<Guid>(type: "uuid", nullable: true),
                    StartedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    FinishedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Result = table.Column<int>(type: "integer", nullable: false),
                    Errors = table.Column<string>(type: "text", nullable: true),
                    CollectedData = table.Column<string>(type: "text", nullable: true),
                    AttemptCount = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Collections", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Collections_WindowsClients_WindowsClientId",
                        column: x => x.WindowsClientId,
                        principalTable: "WindowsClients",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Collections_TenantId",
                table: "Collections",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_Collections_TenantId_IdempotencyKey",
                table: "Collections",
                columns: new[] { "TenantId", "IdempotencyKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Collections_TenantId_PrinterId_StartedAt",
                table: "Collections",
                columns: new[] { "TenantId", "PrinterId", "StartedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Collections_TenantId_WindowsClientId_StartedAt",
                table: "Collections",
                columns: new[] { "TenantId", "WindowsClientId", "StartedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Collections_WindowsClientId",
                table: "Collections",
                column: "WindowsClientId");

            migrationBuilder.CreateIndex(
                name: "IX_PrinterCounters_PrinterId",
                table: "PrinterCounters",
                column: "PrinterId");

            migrationBuilder.CreateIndex(
                name: "IX_PrinterCounters_TenantId",
                table: "PrinterCounters",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_PrinterCounters_TenantId_PrinterId_CounterType_Timestamp",
                table: "PrinterCounters",
                columns: new[] { "TenantId", "PrinterId", "CounterType", "Timestamp" });

            migrationBuilder.CreateIndex(
                name: "IX_PrinterEvents_TenantId",
                table: "PrinterEvents",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_PrinterEvents_TenantId_PrinterId_OccurredAt",
                table: "PrinterEvents",
                columns: new[] { "TenantId", "PrinterId", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_PrinterEvents_TenantId_Type_OccurredAt",
                table: "PrinterEvents",
                columns: new[] { "TenantId", "Type", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_PrinterMovements_PrinterId",
                table: "PrinterMovements",
                column: "PrinterId");

            migrationBuilder.CreateIndex(
                name: "IX_PrinterMovements_TenantId",
                table: "PrinterMovements",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_PrinterMovements_TenantId_PrinterId_OccurredAt",
                table: "PrinterMovements",
                columns: new[] { "TenantId", "PrinterId", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Printers_CustomerId",
                table: "Printers",
                column: "CustomerId");

            migrationBuilder.CreateIndex(
                name: "IX_Printers_LocationId",
                table: "Printers",
                column: "LocationId");

            migrationBuilder.CreateIndex(
                name: "IX_Printers_TenantId",
                table: "Printers",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_Printers_TenantId_CustomerId",
                table: "Printers",
                columns: new[] { "TenantId", "CustomerId" });

            migrationBuilder.CreateIndex(
                name: "IX_Printers_TenantId_LocationId",
                table: "Printers",
                columns: new[] { "TenantId", "LocationId" });

            migrationBuilder.CreateIndex(
                name: "IX_Printers_TenantId_NumeroSerie",
                table: "Printers",
                columns: new[] { "TenantId", "NumeroSerie" });

            migrationBuilder.CreateIndex(
                name: "IX_Printers_TenantId_Status",
                table: "Printers",
                columns: new[] { "TenantId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_WindowsClients_CustomerId",
                table: "WindowsClients",
                column: "CustomerId");

            migrationBuilder.CreateIndex(
                name: "IX_WindowsClients_LocationId",
                table: "WindowsClients",
                column: "LocationId");

            migrationBuilder.CreateIndex(
                name: "IX_WindowsClients_TenantId",
                table: "WindowsClients",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_WindowsClients_TenantId_LocationId",
                table: "WindowsClients",
                columns: new[] { "TenantId", "LocationId" });

            migrationBuilder.CreateIndex(
                name: "IX_WindowsClients_TenantId_State",
                table: "WindowsClients",
                columns: new[] { "TenantId", "State" });

            migrationBuilder.CreateIndex(
                name: "IX_WindowsClients_TenantId_UniqueId",
                table: "WindowsClients",
                columns: new[] { "TenantId", "UniqueId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Collections");

            migrationBuilder.DropTable(
                name: "PrinterCounters");

            migrationBuilder.DropTable(
                name: "PrinterEvents");

            migrationBuilder.DropTable(
                name: "PrinterMovements");

            migrationBuilder.DropTable(
                name: "WindowsClients");

            migrationBuilder.DropTable(
                name: "Printers");
        }
    }
}
