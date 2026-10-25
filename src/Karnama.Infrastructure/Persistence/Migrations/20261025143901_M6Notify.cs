using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Karnama.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class M6Notify : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ExpiryNoticeSent",
                table: "client_packages",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "bot_updates",
                columns: table => new
                {
                    Messenger = table.Column<int>(type: "integer", nullable: false),
                    UpdateId = table.Column<long>(type: "bigint", nullable: false),
                    At = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_bot_updates", x => new { x.Messenger, x.UpdateId });
                });

            migrationBuilder.CreateTable(
                name: "messenger_link_codes",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Messenger = table.Column<int>(type: "integer", nullable: false),
                    CodeHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UsedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_messenger_link_codes", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "messenger_links",
                columns: table => new
                {
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Messenger = table.Column<int>(type: "integer", nullable: false),
                    ChatId = table.Column<long>(type: "bigint", nullable: false),
                    LinkedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_messenger_links", x => new { x.UserId, x.Messenger });
                });

            migrationBuilder.CreateTable(
                name: "notification_prefs",
                columns: table => new
                {
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Channels = table.Column<string>(type: "jsonb", nullable: false),
                    Muted = table.Column<string>(type: "jsonb", nullable: false),
                    QuietFrom = table.Column<TimeOnly>(type: "time without time zone", nullable: true),
                    QuietTo = table.Column<TimeOnly>(type: "time without time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_notification_prefs", x => x.UserId);
                });

            migrationBuilder.CreateTable(
                name: "notifications",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EventKey = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Kind = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    Title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Body = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    Link = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    Important = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ReadAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_notifications", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "outbox",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    Channel = table.Column<int>(type: "integer", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Text = table.Column<string>(type: "character varying(1500)", maxLength: 1500, nullable: false),
                    Link = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    IdempotencyKey = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    Attempts = table.Column<int>(type: "integer", nullable: false),
                    DueAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    SentAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LastError = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_outbox", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "publish_jobs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ClientId = table.Column<Guid>(type: "uuid", nullable: false),
                    OrderId = table.Column<Guid>(type: "uuid", nullable: true),
                    PlatformId = table.Column<Guid>(type: "uuid", nullable: false),
                    Handle = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Management = table.Column<int>(type: "integer", nullable: false),
                    PublishAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Caption = table.Column<string>(type: "character varying(2200)", maxLength: 2200, nullable: false),
                    Hashtags = table.Column<string>(type: "jsonb", nullable: false),
                    FileIds = table.Column<string>(type: "jsonb", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    DueNotifiedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    PublishedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    PostUrl = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    PublishedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    PublishedByClient = table.Column<bool>(type: "boolean", nullable: false),
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
                    table.PrimaryKey("PK_publish_jobs", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_messenger_link_codes_CodeHash",
                table: "messenger_link_codes",
                column: "CodeHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_messenger_links_Messenger_ChatId",
                table: "messenger_links",
                columns: new[] { "Messenger", "ChatId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_notifications_UserId_CreatedAt",
                table: "notifications",
                columns: new[] { "UserId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_notifications_UserId_EventKey",
                table: "notifications",
                columns: new[] { "UserId", "EventKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_notifications_UserId_ReadAt",
                table: "notifications",
                columns: new[] { "UserId", "ReadAt" });

            migrationBuilder.CreateIndex(
                name: "IX_outbox_IdempotencyKey",
                table: "outbox",
                column: "IdempotencyKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_outbox_Status_DueAt",
                table: "outbox",
                columns: new[] { "Status", "DueAt" });

            migrationBuilder.CreateIndex(
                name: "IX_publish_jobs_ClientId_PublishAt",
                table: "publish_jobs",
                columns: new[] { "ClientId", "PublishAt" });

            migrationBuilder.CreateIndex(
                name: "IX_publish_jobs_Status_PublishAt",
                table: "publish_jobs",
                columns: new[] { "Status", "PublishAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "bot_updates");

            migrationBuilder.DropTable(
                name: "messenger_link_codes");

            migrationBuilder.DropTable(
                name: "messenger_links");

            migrationBuilder.DropTable(
                name: "notification_prefs");

            migrationBuilder.DropTable(
                name: "notifications");

            migrationBuilder.DropTable(
                name: "outbox");

            migrationBuilder.DropTable(
                name: "publish_jobs");

            migrationBuilder.DropColumn(
                name: "ExpiryNoticeSent",
                table: "client_packages");
        }
    }
}
