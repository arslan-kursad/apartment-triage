using Microsoft.EntityFrameworkCore.Migrations;
using Pgvector;

#nullable disable

namespace ApartmentTriage.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddEmbeddingVectorToTickets : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Vector>(
                name: "embedding_vector",
                table: "tickets",
                type: "vector(384)",
                nullable: true);

            // HNSW index for cosine similarity search — EF Core Fluent API doesn't support hnsw,
            // raw SQL required per project convention (see InitialSchema for pgvector extension).
            migrationBuilder.Sql(
                "CREATE INDEX ix_tickets_embedding_vector_hnsw " +
                "ON tickets USING hnsw (embedding_vector vector_cosine_ops) " +
                "WITH (m = 16, ef_construction = 64);");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "DROP INDEX IF EXISTS ix_tickets_embedding_vector_hnsw;");

            migrationBuilder.DropColumn(
                name: "embedding_vector",
                table: "tickets");
        }
    }
}
