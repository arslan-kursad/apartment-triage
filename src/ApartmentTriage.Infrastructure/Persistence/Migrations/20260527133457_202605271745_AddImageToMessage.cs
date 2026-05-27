using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ApartmentTriage.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class _202605271745_AddImageToMessage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<byte[]>(
                name: "image_data",
                table: "messages",
                type: "bytea",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "image_mime_type",
                table: "messages",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "image_data",
                table: "messages");

            migrationBuilder.DropColumn(
                name: "image_mime_type",
                table: "messages");
        }
    }
}
