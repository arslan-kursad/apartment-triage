using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ApartmentTriage.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class _202605311901_AddOtpChallenge : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "otp_challenges",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    identifier = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    channel = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    code_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    expires_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    attempt_count = table.Column<int>(type: "integer", nullable: false),
                    consumed_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_otp_challenges", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_otp_challenges_code_hash",
                table: "otp_challenges",
                column: "code_hash");

            migrationBuilder.CreateIndex(
                name: "ix_otp_challenges_identifier_channel_created_at",
                table: "otp_challenges",
                columns: new[] { "identifier", "channel", "created_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "otp_challenges");
        }
    }
}
