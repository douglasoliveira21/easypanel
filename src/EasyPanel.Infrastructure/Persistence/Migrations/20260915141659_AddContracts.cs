using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EasyPanel.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddContracts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Contracts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Number = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    CustomerId = table.Column<Guid>(type: "uuid", nullable: false),
                    StartDate = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    StartDateTicks = table.Column<long>(type: "bigint", nullable: false),
                    EndDate = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    EndDateTicks = table.Column<long>(type: "bigint", nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    Observations = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    CreatedAtTicks = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Contracts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Contracts_Customers_CustomerId",
                        column: x => x.CustomerId,
                        principalTable: "Customers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ContractFranchises",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ContractId = table.Column<Guid>(type: "uuid", nullable: false),
                    CounterType = table.Column<int>(type: "integer", nullable: false),
                    CounterTypeLabel = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    IncludedQuantity = table.Column<long>(type: "bigint", nullable: false),
                    ExcessUnitPrice = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ContractFranchises", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ContractFranchises_Contracts_ContractId",
                        column: x => x.ContractId,
                        principalTable: "Contracts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ContractLocations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ContractId = table.Column<Guid>(type: "uuid", nullable: false),
                    LocationId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ContractLocations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ContractLocations_Contracts_ContractId",
                        column: x => x.ContractId,
                        principalTable: "Contracts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ContractLocations_Locations_LocationId",
                        column: x => x.LocationId,
                        principalTable: "Locations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ContractPrinters",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ContractId = table.Column<Guid>(type: "uuid", nullable: false),
                    PrinterId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ContractPrinters", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ContractPrinters_Contracts_ContractId",
                        column: x => x.ContractId,
                        principalTable: "Contracts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ContractPrinters_Printers_PrinterId",
                        column: x => x.PrinterId,
                        principalTable: "Printers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ContractFranchises_ContractId",
                table: "ContractFranchises",
                column: "ContractId");

            migrationBuilder.CreateIndex(
                name: "IX_ContractFranchises_TenantId_ContractId_CounterType",
                table: "ContractFranchises",
                columns: new[] { "TenantId", "ContractId", "CounterType" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ContractLocations_ContractId",
                table: "ContractLocations",
                column: "ContractId");

            migrationBuilder.CreateIndex(
                name: "IX_ContractLocations_LocationId",
                table: "ContractLocations",
                column: "LocationId");

            migrationBuilder.CreateIndex(
                name: "IX_ContractLocations_TenantId_ContractId_LocationId",
                table: "ContractLocations",
                columns: new[] { "TenantId", "ContractId", "LocationId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ContractLocations_TenantId_LocationId",
                table: "ContractLocations",
                columns: new[] { "TenantId", "LocationId" });

            migrationBuilder.CreateIndex(
                name: "IX_ContractPrinters_ContractId",
                table: "ContractPrinters",
                column: "ContractId");

            migrationBuilder.CreateIndex(
                name: "IX_ContractPrinters_PrinterId",
                table: "ContractPrinters",
                column: "PrinterId");

            migrationBuilder.CreateIndex(
                name: "IX_ContractPrinters_TenantId_ContractId_PrinterId",
                table: "ContractPrinters",
                columns: new[] { "TenantId", "ContractId", "PrinterId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ContractPrinters_TenantId_PrinterId",
                table: "ContractPrinters",
                columns: new[] { "TenantId", "PrinterId" });

            migrationBuilder.CreateIndex(
                name: "IX_Contracts_CustomerId",
                table: "Contracts",
                column: "CustomerId");

            migrationBuilder.CreateIndex(
                name: "IX_Contracts_TenantId",
                table: "Contracts",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_Contracts_TenantId_CustomerId_Status",
                table: "Contracts",
                columns: new[] { "TenantId", "CustomerId", "Status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ContractFranchises");

            migrationBuilder.DropTable(
                name: "ContractLocations");

            migrationBuilder.DropTable(
                name: "ContractPrinters");

            migrationBuilder.DropTable(
                name: "Contracts");
        }
    }
}
