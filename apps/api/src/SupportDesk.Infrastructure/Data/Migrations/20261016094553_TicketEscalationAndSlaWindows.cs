using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SupportDesk.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class TicketEscalationAndSlaWindows : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "AtRiskAtUtc",
                table: "Tickets",
                type: "datetime2(3)",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Revision",
                table: "Tickets",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "SlaStartedAtUtc",
                table: "Tickets",
                type: "datetime2(3)",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "TicketEscalations",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TicketId = table.Column<int>(type: "int", nullable: false),
                    FromPriority = table.Column<int>(type: "int", nullable: false),
                    ToPriority = table.Column<int>(type: "int", nullable: false),
                    FromAgentId = table.Column<int>(type: "int", nullable: true),
                    ToAgentId = table.Column<int>(type: "int", nullable: true),
                    FromDueAtUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    ToDueAtUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    EscalatedBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    EscalatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TicketEscalations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TicketEscalations_Agents_FromAgentId",
                        column: x => x.FromAgentId,
                        principalTable: "Agents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TicketEscalations_Agents_ToAgentId",
                        column: x => x.ToAgentId,
                        principalTable: "Agents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TicketEscalations_Tickets_TicketId",
                        column: x => x.TicketId,
                        principalTable: "Tickets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TicketEscalations_FromAgentId",
                table: "TicketEscalations",
                column: "FromAgentId");

            migrationBuilder.CreateIndex(
                name: "IX_TicketEscalations_Ticket_Time_Id",
                table: "TicketEscalations",
                columns: new[] { "TicketId", "EscalatedAtUtc", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_TicketEscalations_ToAgentId",
                table: "TicketEscalations",
                column: "ToAgentId");

            // Existing due dates were based on creation. Snapshot the original default 25% cutoff.
            migrationBuilder.Sql(@"
                UPDATE [Tickets]
                SET [SlaStartedAtUtc] = [CreatedAtUtc],
                    [AtRiskAtUtc] = CASE WHEN [DueAtUtc] > [CreatedAtUtc]
                        THEN DATEADD(SECOND, CONVERT(int, DATEDIFF_BIG(SECOND, [CreatedAtUtc], [DueAtUtc]) * 0.75), [CreatedAtUtc])
                        ELSE [DueAtUtc] END
                WHERE [DueAtUtc] IS NOT NULL;");

            migrationBuilder.Sql(@"
                CREATE TRIGGER [TR_TicketEscalations_Immutable]
                ON [TicketEscalations] INSTEAD OF UPDATE, DELETE AS
                BEGIN
                    THROW 51000, 'Escalation history is immutable.', 1;
                END;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TicketEscalations");

            migrationBuilder.DropColumn(
                name: "AtRiskAtUtc",
                table: "Tickets");

            migrationBuilder.DropColumn(
                name: "Revision",
                table: "Tickets");

            migrationBuilder.DropColumn(
                name: "SlaStartedAtUtc",
                table: "Tickets");
        }
    }
}
