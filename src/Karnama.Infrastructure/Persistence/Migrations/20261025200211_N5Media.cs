using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Karnama.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class N5Media : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Payload",
                table: "outbox",
                type: "character varying(8000)",
                maxLength: 8000,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "media_assets",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Messenger = table.Column<int>(type: "integer", nullable: false),
                    SenderId = table.Column<Guid>(type: "uuid", nullable: false),
                    Kind = table.Column<int>(type: "integer", nullable: false),
                    FileId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    FileUniqueId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    FileSize = table.Column<long>(type: "bigint", nullable: true),
                    DurationSeconds = table.Column<int>(type: "integer", nullable: true),
                    FileName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    MimeType = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Caption = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    ThumbFileId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    ThumbPath = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    SentAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ReceivedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    SourceChatId = table.Column<long>(type: "bigint", nullable: false),
                    SourceMessageId = table.Column<long>(type: "bigint", nullable: false),
                    ArchiveChatId = table.Column<long>(type: "bigint", nullable: true),
                    ArchiveMessageId = table.Column<long>(type: "bigint", nullable: true),
                    WorkItemId = table.Column<Guid>(type: "uuid", nullable: true),
                    ClientId = table.Column<Guid>(type: "uuid", nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    AssignedToId = table.Column<Guid>(type: "uuid", nullable: true),
                    AssignedById = table.Column<Guid>(type: "uuid", nullable: true),
                    AssignedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    DeliveredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Error = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_media_assets", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_media_assets_Messenger_FileUniqueId",
                table: "media_assets",
                columns: new[] { "Messenger", "FileUniqueId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_media_assets_SenderId",
                table: "media_assets",
                column: "SenderId");

            migrationBuilder.CreateIndex(
                name: "IX_media_assets_Status_Id",
                table: "media_assets",
                columns: new[] { "Status", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_media_assets_WorkItemId",
                table: "media_assets",
                column: "WorkItemId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "media_assets");

            migrationBuilder.DropColumn(
                name: "Payload",
                table: "outbox");
        }
    }
}
