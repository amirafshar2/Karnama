using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Karnama.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class N10Site : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "case_studies",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BusinessName = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    Slug = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    Industry = table.Column<string>(type: "text", nullable: true),
                    City = table.Column<string>(type: "text", nullable: true),
                    Logo = table.Column<string>(type: "text", nullable: true),
                    Handle = table.Column<string>(type: "text", nullable: true),
                    From = table.Column<DateOnly>(type: "date", nullable: true),
                    To = table.Column<DateOnly>(type: "date", nullable: true),
                    Services = table.Column<string>(type: "jsonb", nullable: false),
                    Summary = table.Column<string>(type: "text", nullable: true),
                    FollowersBefore = table.Column<long>(type: "bigint", nullable: true),
                    FollowersAfter = table.Column<long>(type: "bigint", nullable: true),
                    RevenueBefore = table.Column<long>(type: "bigint", nullable: true),
                    RevenueAfter = table.Column<long>(type: "bigint", nullable: true),
                    RevenueAsPercent = table.Column<bool>(type: "boolean", nullable: false),
                    Metrics = table.Column<string>(type: "jsonb", nullable: false),
                    Shots = table.Column<string>(type: "jsonb", nullable: false),
                    Quote = table.Column<string>(type: "text", nullable: true),
                    QuoteBy = table.Column<string>(type: "text", nullable: true),
                    VideoUrl = table.Column<string>(type: "text", nullable: true),
                    Source = table.Column<string>(type: "text", nullable: true),
                    ConsentObtained = table.Column<bool>(type: "boolean", nullable: false),
                    ConsentFile = table.Column<string>(type: "text", nullable: true),
                    IsSample = table.Column<bool>(type: "boolean", nullable: false),
                    Published = table.Column<bool>(type: "boolean", nullable: false),
                    Featured = table.Column<bool>(type: "boolean", nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    DeletedBy = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_case_studies", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "leads",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    Phone = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Business = table.Column<string>(type: "text", nullable: true),
                    Message = table.Column<string>(type: "text", nullable: true),
                    Source = table.Column<string>(type: "text", nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    Note = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_leads", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_case_studies_Slug",
                table: "case_studies",
                column: "Slug",
                unique: true,
                filter: "\"IsDeleted\" = false");

            migrationBuilder.CreateIndex(
                name: "IX_leads_Phone_CreatedAt",
                table: "leads",
                columns: new[] { "Phone", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_leads_Status",
                table: "leads",
                column: "Status");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "case_studies");

            migrationBuilder.DropTable(
                name: "leads");
        }
    }
}
