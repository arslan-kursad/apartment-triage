using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ApartmentTriage.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class _202605311859_AddRoleToResidents : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "role",
                table: "residents",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "None");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "role",
                table: "residents");
        }
    }
}
