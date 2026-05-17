using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ApartmentTriage.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddDomainEntities : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "residents",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    apartment_number = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    display_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    whats_app_number = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    telegram_id = table.Column<long>(type: "bigint", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_residents", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "messages",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    resident_id = table.Column<Guid>(type: "uuid", nullable: false),
                    channel_type = table.Column<string>(type: "text", nullable: false),
                    external_message_id = table.Column<string>(type: "text", nullable: false),
                    raw_text = table.Column<string>(type: "text", nullable: false),
                    emergency_suspected = table.Column<bool>(type: "boolean", nullable: false),
                    matched_phrases = table.Column<string[]>(type: "text[]", nullable: false),
                    received_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    processed_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_messages", x => x.id);
                    table.ForeignKey(
                        name: "fk_messages_residents_resident_id",
                        column: x => x.resident_id,
                        principalTable: "residents",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "tickets",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    resident_id = table.Column<Guid>(type: "uuid", nullable: false),
                    source_message_id = table.Column<Guid>(type: "uuid", nullable: false),
                    category = table.Column<string>(type: "text", nullable: false),
                    severity = table.Column<string>(type: "text", nullable: false),
                    status = table.Column<string>(type: "text", nullable: false),
                    location_hint = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    context = table.Column<string>(type: "text", nullable: true),
                    notes = table.Column<string>(type: "text", nullable: true),
                    secondary_issues_json = table.Column<string>(type: "text", nullable: true),
                    is_emergency = table.Column<bool>(type: "boolean", nullable: false),
                    emergency_confirmed_by_llm = table.Column<bool>(type: "boolean", nullable: false),
                    category_confidence = table.Column<string>(type: "text", nullable: false),
                    emergency_confidence = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_tickets", x => x.id);
                    table.ForeignKey(
                        name: "fk_tickets_messages_source_message_id",
                        column: x => x.source_message_id,
                        principalTable: "messages",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_tickets_residents_resident_id",
                        column: x => x.resident_id,
                        principalTable: "residents",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_messages_channel_type_external_message_id",
                table: "messages",
                columns: new[] { "channel_type", "external_message_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_messages_received_at",
                table: "messages",
                column: "received_at");

            migrationBuilder.CreateIndex(
                name: "ix_messages_resident_id",
                table: "messages",
                column: "resident_id");

            migrationBuilder.CreateIndex(
                name: "ix_residents_telegram_id",
                table: "residents",
                column: "telegram_id",
                unique: true,
                filter: "telegram_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_residents_whats_app_number",
                table: "residents",
                column: "whats_app_number",
                unique: true,
                filter: "whats_app_number IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_tickets_category",
                table: "tickets",
                column: "category");

            migrationBuilder.CreateIndex(
                name: "ix_tickets_created_at",
                table: "tickets",
                column: "created_at");

            migrationBuilder.CreateIndex(
                name: "ix_tickets_is_emergency_status",
                table: "tickets",
                columns: new[] { "is_emergency", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_tickets_resident_id",
                table: "tickets",
                column: "resident_id");

            migrationBuilder.CreateIndex(
                name: "ix_tickets_source_message_id",
                table: "tickets",
                column: "source_message_id");

            migrationBuilder.CreateIndex(
                name: "ix_tickets_status",
                table: "tickets",
                column: "status");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "tickets");

            migrationBuilder.DropTable(
                name: "messages");

            migrationBuilder.DropTable(
                name: "residents");
        }
    }
}
