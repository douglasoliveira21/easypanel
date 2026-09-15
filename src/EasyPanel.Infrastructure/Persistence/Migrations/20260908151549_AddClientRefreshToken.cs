using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EasyPanel.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddClientRefreshToken : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ClientRefreshTokens",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    WindowsClientId = table.Column<Guid>(type: "uuid", nullable: false),
                    TokenHash = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    RevokedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ReplacedByTokenHash = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClientRefreshTokens", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ClientRefreshTokens_WindowsClients_WindowsClientId",
                        column: x => x.WindowsClientId,
                        principalTable: "WindowsClients",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ClientRefreshTokens_TenantId",
                table: "ClientRefreshTokens",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_ClientRefreshTokens_TenantId_WindowsClientId",
                table: "ClientRefreshTokens",
                columns: new[] { "TenantId", "WindowsClientId" });

            migrationBuilder.CreateIndex(
                name: "IX_ClientRefreshTokens_TokenHash",
                table: "ClientRefreshTokens",
                column: "TokenHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ClientRefreshTokens_WindowsClientId",
                table: "ClientRefreshTokens",
                column: "WindowsClientId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ClientRefreshTokens");
        }
    }
}
