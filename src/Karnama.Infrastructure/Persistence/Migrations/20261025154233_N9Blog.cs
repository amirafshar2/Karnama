using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Karnama.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class N9Blog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "blog_categories",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    Slug = table.Column<string>(type: "character varying(70)", maxLength: 70, nullable: false),
                    Description = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    SortOrder = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_blog_categories", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "blog_posts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Title = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    Slug = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    AuthorId = table.Column<Guid>(type: "uuid", nullable: false),
                    CategoryId = table.Column<Guid>(type: "uuid", nullable: true),
                    Tags = table.Column<string>(type: "jsonb", nullable: false),
                    ContentJson = table.Column<string>(type: "text", nullable: false),
                    ContentHtml = table.Column<string>(type: "text", nullable: false),
                    ContentMarkdown = table.Column<string>(type: "text", nullable: false),
                    PlainText = table.Column<string>(type: "text", nullable: false),
                    CoverImage = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    CoverAlt = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    SeoTitle = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    SeoTitleLocked = table.Column<bool>(type: "boolean", nullable: false),
                    MetaDescription = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    MetaLocked = table.Column<bool>(type: "boolean", nullable: false),
                    FocusKeyword = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    KeywordLocked = table.Column<bool>(type: "boolean", nullable: false),
                    SlugLocked = table.Column<bool>(type: "boolean", nullable: false),
                    Toc = table.Column<string>(type: "jsonb", nullable: false),
                    SeoChecks = table.Column<string>(type: "jsonb", nullable: false),
                    Faq = table.Column<string>(type: "jsonb", nullable: false),
                    SeoScore = table.Column<int>(type: "integer", nullable: false),
                    WordCount = table.Column<int>(type: "integer", nullable: false),
                    ReadingMinutes = table.Column<int>(type: "integer", nullable: false),
                    PublishAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    PublishedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ModifiedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
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
                    table.PrimaryKey("PK_blog_posts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "blog_slug_redirects",
                columns: table => new
                {
                    OldSlug = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    PostId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_blog_slug_redirects", x => x.OldSlug);
                });

            migrationBuilder.CreateIndex(
                name: "IX_blog_categories_Slug",
                table: "blog_categories",
                column: "Slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_blog_posts_AuthorId",
                table: "blog_posts",
                column: "AuthorId");

            migrationBuilder.CreateIndex(
                name: "IX_blog_posts_Slug",
                table: "blog_posts",
                column: "Slug",
                unique: true,
                filter: "\"IsDeleted\" = false");

            migrationBuilder.CreateIndex(
                name: "IX_blog_posts_Status_PublishedAt",
                table: "blog_posts",
                columns: new[] { "Status", "PublishedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_blog_slug_redirects_PostId",
                table: "blog_slug_redirects",
                column: "PostId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "blog_categories");

            migrationBuilder.DropTable(
                name: "blog_posts");

            migrationBuilder.DropTable(
                name: "blog_slug_redirects");
        }
    }
}
