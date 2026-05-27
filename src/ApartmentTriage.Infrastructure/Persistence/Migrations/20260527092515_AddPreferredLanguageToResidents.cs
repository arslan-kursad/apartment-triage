using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ApartmentTriage.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPreferredLanguageToResidents : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "preferred_language",
                table: "residents",
                type: "character varying(5)",
                maxLength: 5,
                nullable: false,
                defaultValue: "tr");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "preferred_language",
                table: "residents");
        }
    }
}
