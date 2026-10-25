using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Karnama.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class N3Assets : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "asset_categories",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_asset_categories", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "assets",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    CategoryId = table.Column<Guid>(type: "uuid", nullable: false),
                    Brand = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    Model = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    Serial = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    Tag = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    PurchasedOn = table.Column<DateOnly>(type: "date", nullable: true),
                    PurchasePrice = table.Column<long>(type: "bigint", nullable: true),
                    WarrantyUntil = table.Column<DateOnly>(type: "date", nullable: true),
                    Note = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    HolderId = table.Column<Guid>(type: "uuid", nullable: true),
                    WarrantyNoticeDays = table.Column<int>(type: "integer", nullable: true),
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
                    table.PrimaryKey("PK_assets", x => x.Id);
                    table.ForeignKey(
                        name: "FK_assets_asset_categories_CategoryId",
                        column: x => x.CategoryId,
                        principalTable: "asset_categories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "asset_assignments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AssetId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    HandedBy = table.Column<Guid>(type: "uuid", nullable: false),
                    HandedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ExpectedReturn = table.Column<DateOnly>(type: "date", nullable: true),
                    ConditionOut = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    Accessories = table.Column<string>(type: "jsonb", nullable: false),
                    AcceptedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    State = table.Column<int>(type: "integer", nullable: false),
                    ReturnedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ConditionIn = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    Damage = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    ReturnConfirmedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    TransferToId = table.Column<Guid>(type: "uuid", nullable: true),
                    OverdueNoticeOn = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_asset_assignments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_asset_assignments_assets_AssetId",
                        column: x => x.AssetId,
                        principalTable: "assets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "asset_events",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    AssetId = table.Column<Guid>(type: "uuid", nullable: false),
                    AssignmentId = table.Column<Guid>(type: "uuid", nullable: true),
                    ActorId = table.Column<Guid>(type: "uuid", nullable: true),
                    Kind = table.Column<int>(type: "integer", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: true),
                    Note = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    At = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_asset_events", x => x.Id);
                    table.ForeignKey(
                        name: "FK_asset_events_assets_AssetId",
                        column: x => x.AssetId,
                        principalTable: "assets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "asset_photos",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AssetId = table.Column<Guid>(type: "uuid", nullable: false),
                    AssignmentId = table.Column<Guid>(type: "uuid", nullable: true),
                    Stage = table.Column<int>(type: "integer", nullable: false),
                    FileKey = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ContentType = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    Size = table.Column<long>(type: "bigint", nullable: false),
                    UploadedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    At = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_asset_photos", x => x.Id);
                    table.ForeignKey(
                        name: "FK_asset_photos_assets_AssetId",
                        column: x => x.AssetId,
                        principalTable: "assets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_asset_assignments_TransferToId",
                table: "asset_assignments",
                column: "TransferToId");

            migrationBuilder.CreateIndex(
                name: "IX_asset_assignments_UserId_State",
                table: "asset_assignments",
                columns: new[] { "UserId", "State" });

            migrationBuilder.CreateIndex(
                name: "ux_asset_assignments_open",
                table: "asset_assignments",
                column: "AssetId",
                unique: true,
                filter: "\"State\" IN (1, 2, 3)");

            migrationBuilder.CreateIndex(
                name: "IX_asset_categories_Name",
                table: "asset_categories",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_asset_events_AssetId_Id",
                table: "asset_events",
                columns: new[] { "AssetId", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_asset_photos_AssetId_At",
                table: "asset_photos",
                columns: new[] { "AssetId", "At" });

            migrationBuilder.CreateIndex(
                name: "IX_asset_photos_AssignmentId",
                table: "asset_photos",
                column: "AssignmentId");

            migrationBuilder.CreateIndex(
                name: "IX_assets_CategoryId",
                table: "assets",
                column: "CategoryId");

            migrationBuilder.CreateIndex(
                name: "IX_assets_HolderId",
                table: "assets",
                column: "HolderId");

            migrationBuilder.CreateIndex(
                name: "IX_assets_Serial",
                table: "assets",
                column: "Serial");

            migrationBuilder.CreateIndex(
                name: "IX_assets_Status",
                table: "assets",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_assets_Tag",
                table: "assets",
                column: "Tag",
                unique: true,
                filter: "\"IsDeleted\" = false");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "asset_assignments");

            migrationBuilder.DropTable(
                name: "asset_events");

            migrationBuilder.DropTable(
                name: "asset_photos");

            migrationBuilder.DropTable(
                name: "assets");

            migrationBuilder.DropTable(
                name: "asset_categories");
        }
    }
}
