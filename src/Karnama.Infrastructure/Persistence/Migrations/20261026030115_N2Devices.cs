using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Karnama.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class N2Devices : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "biometric_devices",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    Serial = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Kind = table.Column<int>(type: "integer", nullable: false),
                    Mode = table.Column<int>(type: "integer", nullable: false),
                    Location = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    AllowedIps = table.Column<string>(type: "jsonb", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    LastSeenAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LastIp = table.Column<string>(type: "character varying(45)", maxLength: 45, nullable: true),
                    AttStamp = table.Column<long>(type: "bigint", nullable: false),
                    ClockSkewSeconds = table.Column<int>(type: "integer", nullable: true),
                    SkewWarnedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    OfflineWarnedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    PendingCommands = table.Column<string>(type: "jsonb", nullable: false),
                    NextCommandId = table.Column<int>(type: "integer", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_biometric_devices", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "device_pins",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Pin = table.Column<string>(type: "character varying(9)", maxLength: 9, nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_device_pins", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "device_punches",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    DeviceId = table.Column<Guid>(type: "uuid", nullable: false),
                    Pin = table.Column<string>(type: "character varying(9)", maxLength: 9, nullable: false),
                    At = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    ReceivedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: true),
                    Outcome = table.Column<int>(type: "integer", nullable: false),
                    Note = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_device_punches", x => x.Id);
                    table.ForeignKey(
                        name: "FK_device_punches_biometric_devices_DeviceId",
                        column: x => x.DeviceId,
                        principalTable: "biometric_devices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_biometric_devices_Serial",
                table: "biometric_devices",
                column: "Serial",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_device_pins_Pin",
                table: "device_pins",
                column: "Pin",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_device_pins_UserId",
                table: "device_pins",
                column: "UserId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_device_punches_At",
                table: "device_punches",
                column: "At");

            migrationBuilder.CreateIndex(
                name: "IX_device_punches_DeviceId_Pin_At",
                table: "device_punches",
                columns: new[] { "DeviceId", "Pin", "At" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_device_punches_Outcome_At",
                table: "device_punches",
                columns: new[] { "Outcome", "At" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "device_pins");

            migrationBuilder.DropTable(
                name: "device_punches");

            migrationBuilder.DropTable(
                name: "biometric_devices");
        }
    }
}
