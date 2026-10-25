using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Karnama.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class N4Vault : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Purpose",
                table: "otp_challenges",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "client_credentials",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ClientId = table.Column<Guid>(type: "uuid", nullable: false),
                    PlatformId = table.Column<Guid>(type: "uuid", nullable: false),
                    Handle = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    BusinessSuiteAccess = table.Column<bool>(type: "boolean", nullable: false),
                    Cipher = table.Column<byte[]>(type: "bytea", maxLength: 4096, nullable: true),
                    WrappedKey = table.Column<byte[]>(type: "bytea", maxLength: 128, nullable: true),
                    KeyVersion = table.Column<int>(type: "integer", nullable: false),
                    Source = table.Column<int>(type: "integer", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    LastRevealedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LastRevealedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_client_credentials", x => x.Id);
                    table.ForeignKey(
                        name: "FK_client_credentials_clients_ClientId",
                        column: x => x.ClientId,
                        principalTable: "clients",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_client_credentials_platforms_PlatformId",
                        column: x => x.PlatformId,
                        principalTable: "platforms",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_client_credentials_ClientId_PlatformId_Handle",
                table: "client_credentials",
                columns: new[] { "ClientId", "PlatformId", "Handle" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_client_credentials_PlatformId",
                table: "client_credentials",
                column: "PlatformId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "client_credentials");

            migrationBuilder.DropColumn(
                name: "Purpose",
                table: "otp_challenges");
        }
    }
}
