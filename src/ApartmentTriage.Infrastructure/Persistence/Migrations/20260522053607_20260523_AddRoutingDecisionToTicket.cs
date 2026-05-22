using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ApartmentTriage.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class _20260523_AddRoutingDecisionToTicket : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ambiguity_reasons_json",
                table: "tickets",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "routing_action",
                table: "tickets",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ambiguity_reasons_json",
                table: "tickets");

            migrationBuilder.DropColumn(
                name: "routing_action",
                table: "tickets");
        }
    }
}
