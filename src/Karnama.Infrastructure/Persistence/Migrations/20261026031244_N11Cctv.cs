using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Karnama.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class N11Cctv : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "cameras",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    Location = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    Path = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    MainCipher = table.Column<byte[]>(type: "bytea", nullable: false),
                    MainKey = table.Column<byte[]>(type: "bytea", nullable: false),
                    SubCipher = table.Column<byte[]>(type: "bytea", nullable: true),
                    SubKey = table.Column<byte[]>(type: "bytea", nullable: true),
                    KeyVersion = table.Column<int>(type: "integer", nullable: false),
                    MainMasked = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    SubMasked = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    Online = table.Column<bool>(type: "boolean", nullable: false),
                    LastOnlineAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    OfflineWarnedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_cameras", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_cameras_Path",
                table: "cameras",
                column: "Path",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "cameras");
        }
    }
}
