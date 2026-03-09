using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Dx7Api.Migrations
{
    /// <inheritdoc />
    public partial class ShiftManagement : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ShiftSchedules",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    ClientId = table.Column<Guid>(type: "uuid", nullable: false),
                    ScheduleDate = table.Column<DateOnly>(type: "date", nullable: false),
                    ShiftNumber = table.Column<int>(type: "integer", nullable: false),
                    ShiftLabel = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    StartTime = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    EndTime = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    MaxChairs = table.Column<int>(type: "integer", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    Notes = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ShiftSchedules", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ShiftSchedules_Clients_ClientId",
                        column: x => x.ClientId,
                        principalTable: "Clients",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ShiftSchedules_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ShiftNurseAssignments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    ShiftScheduleId = table.Column<Guid>(type: "uuid", nullable: false),
                    NurseUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    AssignmentRole = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    AssignedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    AssignedBy = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ShiftNurseAssignments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ShiftNurseAssignments_ShiftSchedules_ShiftScheduleId",
                        column: x => x.ShiftScheduleId,
                        principalTable: "ShiftSchedules",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ShiftNurseAssignments_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ShiftNurseAssignments_Users_AssignedBy",
                        column: x => x.AssignedBy,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ShiftNurseAssignments_Users_NurseUserId",
                        column: x => x.NurseUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ShiftNurseAssignments_AssignedBy",
                table: "ShiftNurseAssignments",
                column: "AssignedBy");

            migrationBuilder.CreateIndex(
                name: "IX_ShiftNurseAssignments_NurseUserId",
                table: "ShiftNurseAssignments",
                column: "NurseUserId");

            migrationBuilder.CreateIndex(
                name: "IX_ShiftNurseAssignments_ShiftScheduleId",
                table: "ShiftNurseAssignments",
                column: "ShiftScheduleId");

            migrationBuilder.CreateIndex(
                name: "IX_ShiftNurseAssignments_TenantId",
                table: "ShiftNurseAssignments",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_ShiftSchedules_ClientId",
                table: "ShiftSchedules",
                column: "ClientId");

            migrationBuilder.CreateIndex(
                name: "IX_ShiftSchedules_TenantId",
                table: "ShiftSchedules",
                column: "TenantId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ShiftNurseAssignments");

            migrationBuilder.DropTable(
                name: "ShiftSchedules");
        }
    }
}
