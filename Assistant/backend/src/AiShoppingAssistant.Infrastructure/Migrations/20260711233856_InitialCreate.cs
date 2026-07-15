using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AiShoppingAssistant.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "conversation_sessions",
                columns: table => new
                {
                    SessionId = table.Column<Guid>(type: "uuid", nullable: false),
                    CustomerId = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_conversation_sessions", x => x.SessionId);
                });

            migrationBuilder.CreateTable(
                name: "conversation_turns",
                columns: table => new
                {
                    TurnId = table.Column<Guid>(type: "uuid", nullable: false),
                    SessionId = table.Column<Guid>(type: "uuid", nullable: false),
                    TurnIndex = table.Column<int>(type: "integer", nullable: false),
                    UserMessage = table.Column<string>(type: "text", nullable: false),
                    ToolCallsExecuted = table.Column<string>(type: "jsonb", nullable: false),
                    RetrievedSimilarityScores = table.Column<string>(type: "jsonb", nullable: true),
                    AssistantMessage = table.Column<string>(type: "text", nullable: false),
                    EscalateToHuman = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_conversation_turns", x => x.TurnId);
                    table.ForeignKey(
                        name: "FK_conversation_turns_conversation_sessions_SessionId",
                        column: x => x.SessionId,
                        principalTable: "conversation_sessions",
                        principalColumn: "SessionId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_conversation_turns_SessionId_TurnIndex",
                table: "conversation_turns",
                columns: new[] { "SessionId", "TurnIndex" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "conversation_turns");

            migrationBuilder.DropTable(
                name: "conversation_sessions");
        }
    }
}
