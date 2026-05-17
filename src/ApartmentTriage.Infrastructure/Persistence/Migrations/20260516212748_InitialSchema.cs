using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ApartmentTriage.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // pgvector extension — raw SQL per convention (no EF abstraction)
            migrationBuilder.Sql("CREATE EXTENSION IF NOT EXISTS vector;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Extension intentionally NOT dropped on rollback (may be used by other schemas)
        }
    }
}
