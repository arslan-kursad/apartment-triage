using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ApartmentTriage.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class _202605311906_DropTelegramUsername : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "telegram_username",
                table: "residents");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "telegram_username",
                table: "residents",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);
        }
    }
}
