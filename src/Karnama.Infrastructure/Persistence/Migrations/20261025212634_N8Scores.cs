using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Karnama.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class N8Scores : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<byte>(
                name: "ClientStars",
                table: "work_items",
                type: "smallint",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "FirstSubmittedAt",
                table: "work_items",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<byte>(
                name: "ManagerStars",
                table: "work_items",
                type: "smallint",
                nullable: true);

            // Existing work: the last hand-in is the best we know about the first one.
            migrationBuilder.Sql("UPDATE work_items SET \"FirstSubmittedAt\" = \"SubmittedAt\" WHERE \"SubmittedAt\" IS NOT NULL;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ClientStars",
                table: "work_items");

            migrationBuilder.DropColumn(
                name: "FirstSubmittedAt",
                table: "work_items");

            migrationBuilder.DropColumn(
                name: "ManagerStars",
                table: "work_items");
        }
    }
}
