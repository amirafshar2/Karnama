using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Karnama.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class M2Catalog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "clients",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BusinessName = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Industry = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    ContactName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Mobile = table.Column<string>(type: "character varying(11)", maxLength: 11, nullable: false),
                    City = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    InternalNote = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    PortalUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    DeletedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    Accounts = table.Column<string>(type: "jsonb", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_clients", x => x.Id);
                    table.ForeignKey(
                        name: "FK_clients_users_PortalUserId",
                        column: x => x.PortalUserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "packages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "jsonb", nullable: false),
                    Slug = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    Sku = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    Category = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    Tags = table.Column<List<string>>(type: "text[]", nullable: false),
                    ShortDescription = table.Column<string>(type: "jsonb", nullable: false),
                    DescriptionMarkdown = table.Column<string>(type: "character varying(20000)", maxLength: 20000, nullable: true),
                    DescriptionHtml = table.Column<string>(type: "character varying(60000)", maxLength: 60000, nullable: true),
                    Price = table.Column<long>(type: "bigint", nullable: false),
                    CompareAtPrice = table.Column<long>(type: "bigint", nullable: true),
                    SpecialPrice = table.Column<long>(type: "bigint", nullable: true),
                    SpecialFrom = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    SpecialTo = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Period = table.Column<int>(type: "integer", nullable: false),
                    ValidityDays = table.Column<int>(type: "integer", nullable: false),
                    SaleFrom = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    SaleTo = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Capacity = table.Column<int>(type: "integer", nullable: true),
                    PlatformIds = table.Column<List<Guid>>(type: "uuid[]", nullable: false),
                    DefaultManagement = table.Column<int>(type: "integer", nullable: false),
                    Access = table.Column<int>(type: "integer", nullable: false),
                    ConsultSessions = table.Column<int>(type: "integer", nullable: false),
                    RevisionsPerItem = table.Column<int>(type: "integer", nullable: false),
                    DeliveryHours = table.Column<int>(type: "integer", nullable: false),
                    Features = table.Column<string>(type: "jsonb", nullable: false),
                    Faq = table.Column<string>(type: "jsonb", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    ShowOnSite = table.Column<bool>(type: "boolean", nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
                    Badge = table.Column<int>(type: "integer", nullable: false),
                    SeoTitle = table.Column<string>(type: "character varying(70)", maxLength: 70, nullable: true),
                    SeoDescription = table.Column<string>(type: "character varying(170)", maxLength: 170, nullable: true),
                    CoverImage = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    GalleryIds = table.Column<List<string>>(type: "text[]", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    NameSearch = table.Column<string>(type: "text", nullable: true, computedColumnSql: "(\"Name\")::text", stored: true),
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
                    table.PrimaryKey("PK_packages", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "platforms",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Key = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    Name = table.Column<string>(type: "jsonb", nullable: false),
                    UrlTemplate = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
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
                    table.PrimaryKey("PK_platforms", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "service_types",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "jsonb", nullable: false),
                    Description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    Icon = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    BasePrice = table.Column<long>(type: "bigint", nullable: true),
                    ConsumesQuota = table.Column<bool>(type: "boolean", nullable: false),
                    Weight = table.Column<int>(type: "integer", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
                    FormSchemaJson = table.Column<string>(type: "jsonb", nullable: false),
                    FormVersion = table.Column<int>(type: "integer", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    NameSearch = table.Column<string>(type: "text", nullable: true, computedColumnSql: "(\"Name\")::text", stored: true),
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
                    table.PrimaryKey("PK_service_types", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "client_packages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ClientId = table.Column<Guid>(type: "uuid", nullable: false),
                    PackageId = table.Column<Guid>(type: "uuid", nullable: false),
                    PackageName = table.Column<string>(type: "jsonb", nullable: false),
                    Price = table.Column<long>(type: "bigint", nullable: false),
                    QuotasJson = table.Column<string>(type: "jsonb", nullable: false),
                    Access = table.Column<int>(type: "integer", nullable: false),
                    StartsAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    EndsAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    Note = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
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
                    table.PrimaryKey("PK_client_packages", x => x.Id);
                    table.ForeignKey(
                        name: "FK_client_packages_clients_ClientId",
                        column: x => x.ClientId,
                        principalTable: "clients",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_client_packages_packages_PackageId",
                        column: x => x.PackageId,
                        principalTable: "packages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "package_quotas",
                columns: table => new
                {
                    PackageId = table.Column<Guid>(type: "uuid", nullable: false),
                    ServiceTypeId = table.Column<Guid>(type: "uuid", nullable: false),
                    Count = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_package_quotas", x => new { x.PackageId, x.ServiceTypeId });
                    table.ForeignKey(
                        name: "FK_package_quotas_packages_PackageId",
                        column: x => x.PackageId,
                        principalTable: "packages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_package_quotas_service_types_ServiceTypeId",
                        column: x => x.ServiceTypeId,
                        principalTable: "service_types",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "service_form_versions",
                columns: table => new
                {
                    ServiceTypeId = table.Column<Guid>(type: "uuid", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    SchemaJson = table.Column<string>(type: "jsonb", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_service_form_versions", x => new { x.ServiceTypeId, x.Version });
                    table.ForeignKey(
                        name: "FK_service_form_versions_service_types_ServiceTypeId",
                        column: x => x.ServiceTypeId,
                        principalTable: "service_types",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "workflow_stages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ServiceTypeId = table.Column<Guid>(type: "uuid", nullable: false),
                    Order = table.Column<int>(type: "integer", nullable: false),
                    Name = table.Column<string>(type: "jsonb", nullable: false),
                    RoleId = table.Column<Guid>(type: "uuid", nullable: true),
                    EstimatedMinutes = table.Column<int>(type: "integer", nullable: false),
                    SlaHours = table.Column<int>(type: "integer", nullable: true),
                    NeedsAdminReview = table.Column<bool>(type: "boolean", nullable: false),
                    NeedsClientApproval = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_workflow_stages", x => x.Id);
                    table.ForeignKey(
                        name: "FK_workflow_stages_roles_RoleId",
                        column: x => x.RoleId,
                        principalTable: "roles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_workflow_stages_service_types_ServiceTypeId",
                        column: x => x.ServiceTypeId,
                        principalTable: "service_types",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_client_packages_ClientId_Status",
                table: "client_packages",
                columns: new[] { "ClientId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_client_packages_EndsAt",
                table: "client_packages",
                column: "EndsAt");

            migrationBuilder.CreateIndex(
                name: "IX_client_packages_PackageId_Status",
                table: "client_packages",
                columns: new[] { "PackageId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_clients_BusinessName",
                table: "clients",
                column: "BusinessName");

            migrationBuilder.CreateIndex(
                name: "IX_clients_Mobile",
                table: "clients",
                column: "Mobile");

            migrationBuilder.CreateIndex(
                name: "IX_clients_PortalUserId",
                table: "clients",
                column: "PortalUserId");

            migrationBuilder.CreateIndex(
                name: "IX_package_quotas_ServiceTypeId",
                table: "package_quotas",
                column: "ServiceTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_packages_Sku",
                table: "packages",
                column: "Sku",
                unique: true,
                filter: "\"IsDeleted\" = false");

            migrationBuilder.CreateIndex(
                name: "IX_packages_Slug",
                table: "packages",
                column: "Slug",
                unique: true,
                filter: "\"IsDeleted\" = false");

            migrationBuilder.CreateIndex(
                name: "IX_packages_Status_SortOrder",
                table: "packages",
                columns: new[] { "Status", "SortOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_platforms_Key",
                table: "platforms",
                column: "Key",
                unique: true,
                filter: "\"IsDeleted\" = false");

            migrationBuilder.CreateIndex(
                name: "IX_workflow_stages_RoleId",
                table: "workflow_stages",
                column: "RoleId");

            migrationBuilder.CreateIndex(
                name: "IX_workflow_stages_ServiceTypeId_Order",
                table: "workflow_stages",
                columns: new[] { "ServiceTypeId", "Order" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "client_packages");

            migrationBuilder.DropTable(
                name: "package_quotas");

            migrationBuilder.DropTable(
                name: "platforms");

            migrationBuilder.DropTable(
                name: "service_form_versions");

            migrationBuilder.DropTable(
                name: "workflow_stages");

            migrationBuilder.DropTable(
                name: "clients");

            migrationBuilder.DropTable(
                name: "packages");

            migrationBuilder.DropTable(
                name: "service_types");
        }
    }
}
