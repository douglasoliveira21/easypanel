using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EasyPanel.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAlerting : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "CreatedAtTicks",
                table: "PrinterEvents",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            // Backfill (design "Migração e índices"): CreatedAtTicks dos
            // PrinterEvents já existentes, calculado a partir de CreatedAt.
            // 621355968000000000 = ticks (.NET, UTC) de 1970-01-01T00:00:00.
            migrationBuilder.Sql(
                """
                UPDATE "PrinterEvents"
                SET "CreatedAtTicks" = 621355968000000000 + CAST(ROUND(EXTRACT(EPOCH FROM "CreatedAt") * 10000000) AS bigint);
                """);

            migrationBuilder.CreateTable(
                name: "AlertEngineCheckpoints",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    LastProcessedEventTicks = table.Column<long>(type: "bigint", nullable: false),
                    LastProcessedEventId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AlertEngineCheckpoints", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AlertRules",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    EventTypesCsv = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ScopeType = table.Column<int>(type: "integer", nullable: false),
                    ScopeLocationId = table.Column<Guid>(type: "uuid", nullable: true),
                    ScopePrinterId = table.Column<Guid>(type: "uuid", nullable: true),
                    ScopeWindowsClientId = table.Column<Guid>(type: "uuid", nullable: true),
                    Severity = table.Column<int>(type: "integer", nullable: false),
                    ThresholdCount = table.Column<int>(type: "integer", nullable: true),
                    ThresholdWindowMinutes = table.Column<int>(type: "integer", nullable: true),
                    AutoResolve = table.Column<bool>(type: "boolean", nullable: false),
                    EmailEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    EmailRecipientsCsv = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    WebhookEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    WebhookUrl = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    WebhookSecret = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AlertRules", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AlertRules_Locations_ScopeLocationId",
                        column: x => x.ScopeLocationId,
                        principalTable: "Locations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AlertRules_Printers_ScopePrinterId",
                        column: x => x.ScopePrinterId,
                        principalTable: "Printers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AlertRules_WindowsClients_ScopeWindowsClientId",
                        column: x => x.ScopeWindowsClientId,
                        principalTable: "WindowsClients",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AlertSilences",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AlertRuleId = table.Column<Guid>(type: "uuid", nullable: true),
                    PrinterId = table.Column<Guid>(type: "uuid", nullable: true),
                    WindowsClientId = table.Column<Guid>(type: "uuid", nullable: true),
                    StartsAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    EndsAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    EndedEarlyAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    EndedEarlyByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AlertSilences", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AlertSilences_AlertRules_AlertRuleId",
                        column: x => x.AlertRuleId,
                        principalTable: "AlertRules",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AlertSilences_Printers_PrinterId",
                        column: x => x.PrinterId,
                        principalTable: "Printers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AlertSilences_WindowsClients_WindowsClientId",
                        column: x => x.WindowsClientId,
                        principalTable: "WindowsClients",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Alerts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AlertRuleId = table.Column<Guid>(type: "uuid", nullable: false),
                    Severity = table.Column<int>(type: "integer", nullable: false),
                    State = table.Column<int>(type: "integer", nullable: false),
                    PrinterId = table.Column<Guid>(type: "uuid", nullable: true),
                    WindowsClientId = table.Column<Guid>(type: "uuid", nullable: true),
                    FirstOccurrenceAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    FirstOccurrenceAtTicks = table.Column<long>(type: "bigint", nullable: false),
                    LastOccurrenceAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LastOccurrenceAtTicks = table.Column<long>(type: "bigint", nullable: false),
                    OccurrenceCount = table.Column<int>(type: "integer", nullable: false),
                    AcknowledgedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    AcknowledgedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ResolvedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ResolvedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    AutoResolved = table.Column<bool>(type: "boolean", nullable: false),
                    ResolutionNote = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Alerts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Alerts_AlertRules_AlertRuleId",
                        column: x => x.AlertRuleId,
                        principalTable: "AlertRules",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Alerts_Printers_PrinterId",
                        column: x => x.PrinterId,
                        principalTable: "Printers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Alerts_WindowsClients_WindowsClientId",
                        column: x => x.WindowsClientId,
                        principalTable: "WindowsClients",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AlertNotificationAttempts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AlertId = table.Column<Guid>(type: "uuid", nullable: false),
                    Channel = table.Column<int>(type: "integer", nullable: false),
                    Outcome = table.Column<int>(type: "integer", nullable: false),
                    AttemptNumber = table.Column<int>(type: "integer", nullable: false),
                    HttpStatusCode = table.Column<int>(type: "integer", nullable: true),
                    ErrorSummary = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    AttemptedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    AttemptedAtTicks = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AlertNotificationAttempts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AlertNotificationAttempts_Alerts_AlertId",
                        column: x => x.AlertId,
                        principalTable: "Alerts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AlertNotificationOutbox",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AlertId = table.Column<Guid>(type: "uuid", nullable: false),
                    Channel = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    AttemptCount = table.Column<int>(type: "integer", nullable: false),
                    NextAttemptAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AlertNotificationOutbox", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AlertNotificationOutbox_Alerts_AlertId",
                        column: x => x.AlertId,
                        principalTable: "Alerts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AlertTransitions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AlertId = table.Column<Guid>(type: "uuid", nullable: false),
                    FromState = table.Column<int>(type: "integer", nullable: false),
                    ToState = table.Column<int>(type: "integer", nullable: false),
                    ActorUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    OccurredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Note = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AlertTransitions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AlertTransitions_Alerts_AlertId",
                        column: x => x.AlertId,
                        principalTable: "Alerts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PrinterEvents_CreatedAtTicks",
                table: "PrinterEvents",
                column: "CreatedAtTicks");

            migrationBuilder.CreateIndex(
                name: "IX_AlertNotificationAttempts_AlertId",
                table: "AlertNotificationAttempts",
                column: "AlertId");

            migrationBuilder.CreateIndex(
                name: "IX_AlertNotificationAttempts_TenantId",
                table: "AlertNotificationAttempts",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_AlertNotificationAttempts_TenantId_AlertId_AttemptedAtTicks",
                table: "AlertNotificationAttempts",
                columns: new[] { "TenantId", "AlertId", "AttemptedAtTicks" });

            migrationBuilder.CreateIndex(
                name: "IX_AlertNotificationOutbox_AlertId",
                table: "AlertNotificationOutbox",
                column: "AlertId");

            migrationBuilder.CreateIndex(
                name: "IX_AlertNotificationOutbox_Status_NextAttemptAt",
                table: "AlertNotificationOutbox",
                columns: new[] { "Status", "NextAttemptAt" });

            migrationBuilder.CreateIndex(
                name: "IX_AlertNotificationOutbox_TenantId",
                table: "AlertNotificationOutbox",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_AlertRules_ScopeLocationId",
                table: "AlertRules",
                column: "ScopeLocationId");

            migrationBuilder.CreateIndex(
                name: "IX_AlertRules_ScopePrinterId",
                table: "AlertRules",
                column: "ScopePrinterId");

            migrationBuilder.CreateIndex(
                name: "IX_AlertRules_ScopeWindowsClientId",
                table: "AlertRules",
                column: "ScopeWindowsClientId");

            migrationBuilder.CreateIndex(
                name: "IX_AlertRules_TenantId",
                table: "AlertRules",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_AlertRules_TenantId_IsActive",
                table: "AlertRules",
                columns: new[] { "TenantId", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_AlertRules_TenantId_ScopeType",
                table: "AlertRules",
                columns: new[] { "TenantId", "ScopeType" });

            migrationBuilder.CreateIndex(
                name: "IX_AlertSilences_AlertRuleId",
                table: "AlertSilences",
                column: "AlertRuleId");

            migrationBuilder.CreateIndex(
                name: "IX_AlertSilences_PrinterId",
                table: "AlertSilences",
                column: "PrinterId");

            migrationBuilder.CreateIndex(
                name: "IX_AlertSilences_TenantId",
                table: "AlertSilences",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_AlertSilences_TenantId_AlertRuleId",
                table: "AlertSilences",
                columns: new[] { "TenantId", "AlertRuleId" });

            migrationBuilder.CreateIndex(
                name: "IX_AlertSilences_TenantId_PrinterId",
                table: "AlertSilences",
                columns: new[] { "TenantId", "PrinterId" });

            migrationBuilder.CreateIndex(
                name: "IX_AlertSilences_TenantId_WindowsClientId",
                table: "AlertSilences",
                columns: new[] { "TenantId", "WindowsClientId" });

            migrationBuilder.CreateIndex(
                name: "IX_AlertSilences_WindowsClientId",
                table: "AlertSilences",
                column: "WindowsClientId");

            migrationBuilder.CreateIndex(
                name: "IX_AlertTransitions_AlertId",
                table: "AlertTransitions",
                column: "AlertId");

            migrationBuilder.CreateIndex(
                name: "IX_AlertTransitions_TenantId",
                table: "AlertTransitions",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_AlertTransitions_TenantId_AlertId_OccurredAt",
                table: "AlertTransitions",
                columns: new[] { "TenantId", "AlertId", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Alerts_AlertRuleId",
                table: "Alerts",
                column: "AlertRuleId");

            migrationBuilder.CreateIndex(
                name: "IX_Alerts_PrinterId",
                table: "Alerts",
                column: "PrinterId");

            migrationBuilder.CreateIndex(
                name: "IX_Alerts_TenantId",
                table: "Alerts",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_Alerts_TenantId_AlertRuleId_PrinterId_WindowsClientId_State",
                table: "Alerts",
                columns: new[] { "TenantId", "AlertRuleId", "PrinterId", "WindowsClientId", "State" });

            migrationBuilder.CreateIndex(
                name: "IX_Alerts_TenantId_State_LastOccurrenceAtTicks",
                table: "Alerts",
                columns: new[] { "TenantId", "State", "LastOccurrenceAtTicks" });

            migrationBuilder.CreateIndex(
                name: "IX_Alerts_WindowsClientId",
                table: "Alerts",
                column: "WindowsClientId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AlertEngineCheckpoints");

            migrationBuilder.DropTable(
                name: "AlertNotificationAttempts");

            migrationBuilder.DropTable(
                name: "AlertNotificationOutbox");

            migrationBuilder.DropTable(
                name: "AlertSilences");

            migrationBuilder.DropTable(
                name: "AlertTransitions");

            migrationBuilder.DropTable(
                name: "Alerts");

            migrationBuilder.DropTable(
                name: "AlertRules");

            migrationBuilder.DropIndex(
                name: "IX_PrinterEvents_CreatedAtTicks",
                table: "PrinterEvents");

            migrationBuilder.DropColumn(
                name: "CreatedAtTicks",
                table: "PrinterEvents");
        }
    }
}
