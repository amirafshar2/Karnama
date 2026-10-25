using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Karnama.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class N7Crm : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_leads_Phone_CreatedAt",
                table: "leads");

            migrationBuilder.DropIndex(
                name: "IX_leads_Status",
                table: "leads");

            migrationBuilder.AlterColumn<string>(
                name: "Source",
                table: "leads",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "Phone",
                table: "leads",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(20)",
                oldMaxLength: 20);

            migrationBuilder.AlterColumn<string>(
                name: "Note",
                table: "leads",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "Name",
                table: "leads",
                type: "character varying(80)",
                maxLength: 80,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(60)",
                oldMaxLength: 60);

            migrationBuilder.AlterColumn<string>(
                name: "Message",
                table: "leads",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "Business",
                table: "leads",
                type: "character varying(80)",
                maxLength: 80,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AddColumn<long>(
                name: "Budget",
                table: "leads",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "City",
                table: "leads",
                type: "character varying(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ClientId",
                table: "leads",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ClosedAt",
                table: "leads",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "FollowUpRemindedFor",
                table: "leads",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Industry",
                table: "leads",
                type: "character varying(60)",
                maxLength: 60,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Instagram",
                table: "leads",
                type: "character varying(60)",
                maxLength: 60,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "LastActivityAt",
                table: "leads",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTimeOffset(new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)));

            migrationBuilder.AddColumn<string>(
                name: "LostReason",
                table: "leads",
                type: "character varying(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "MessengerChatId",
                table: "leads",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "NextFollowUpAt",
                table: "leads",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "OwnerId",
                table: "leads",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "SourceId",
                table: "leads",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "StageId",
                table: "leads",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<string>(
                name: "Tags",
                table: "leads",
                type: "jsonb",
                nullable: false,
                defaultValue: "[]");

            migrationBuilder.CreateTable(
                name: "lead_activities",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    LeadId = table.Column<Guid>(type: "uuid", nullable: false),
                    ActorId = table.Column<Guid>(type: "uuid", nullable: true),
                    Kind = table.Column<int>(type: "integer", nullable: false),
                    Direction = table.Column<int>(type: "integer", nullable: true),
                    DurationSeconds = table.Column<int>(type: "integer", nullable: true),
                    Outcome = table.Column<int>(type: "integer", nullable: true),
                    Channel = table.Column<int>(type: "integer", nullable: true),
                    Note = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    FromId = table.Column<Guid>(type: "uuid", nullable: true),
                    ToId = table.Column<Guid>(type: "uuid", nullable: true),
                    At = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_lead_activities", x => x.Id);
                    table.ForeignKey(
                        name: "FK_lead_activities_leads_LeadId",
                        column: x => x.LeadId,
                        principalTable: "leads",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "lead_sources",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_lead_sources", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "lead_stages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Kind = table.Column<int>(type: "integer", nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_lead_stages", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_leads_MessengerChatId",
                table: "leads",
                column: "MessengerChatId");

            migrationBuilder.CreateIndex(
                name: "IX_leads_NextFollowUpAt",
                table: "leads",
                column: "NextFollowUpAt");

            migrationBuilder.CreateIndex(
                name: "IX_leads_OwnerId_StageId",
                table: "leads",
                columns: new[] { "OwnerId", "StageId" });

            migrationBuilder.CreateIndex(
                name: "IX_leads_Phone",
                table: "leads",
                column: "Phone");

            migrationBuilder.CreateIndex(
                name: "IX_leads_SourceId",
                table: "leads",
                column: "SourceId");

            migrationBuilder.CreateIndex(
                name: "IX_leads_StageId",
                table: "leads",
                column: "StageId");

            migrationBuilder.CreateIndex(
                name: "IX_lead_activities_Kind_At",
                table: "lead_activities",
                columns: new[] { "Kind", "At" });

            migrationBuilder.CreateIndex(
                name: "IX_lead_activities_LeadId_Id",
                table: "lead_activities",
                columns: new[] { "LeadId", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_lead_sources_Name",
                table: "lead_sources",
                column: "Name",
                unique: true);

            // Starting pipeline + sources (fixed ids, same as CrmDefaults), then the N10 leads move onto it.
            migrationBuilder.Sql("""
                INSERT INTO lead_stages ("Id", "Name", "Kind", "SortOrder", "IsActive") VALUES
                  ('0193a000-0000-7000-8000-000000000001', 'جدید', 1, 10, true),
                  ('0193a000-0000-7000-8000-000000000002', 'تماس اول', 1, 20, true),
                  ('0193a000-0000-7000-8000-000000000003', 'جلسه / مشاوره', 1, 30, true),
                  ('0193a000-0000-7000-8000-000000000004', 'پیشنهاد قیمت', 1, 40, true),
                  ('0193a000-0000-7000-8000-000000000005', 'مذاکره', 1, 50, true),
                  ('0193a000-0000-7000-8000-000000000006', 'بسته شد (برد)', 2, 60, true),
                  ('0193a000-0000-7000-8000-000000000007', 'از دست رفت', 3, 70, true)
                ON CONFLICT DO NOTHING;
                INSERT INTO lead_sources ("Id", "Name", "IsActive", "SortOrder") VALUES
                  ('0193a000-0000-7000-8000-000000000101', 'معرفی', true, 10),
                  ('0193a000-0000-7000-8000-000000000102', 'اینستاگرام', true, 20),
                  ('0193a000-0000-7000-8000-000000000103', 'تماس ورودی', true, 30),
                  ('0193a000-0000-7000-8000-000000000104', 'فرم سایت', true, 40),
                  ('0193a000-0000-7000-8000-000000000105', 'حضوری', true, 50),
                  ('0193a000-0000-7000-8000-000000000106', 'ربات', true, 60)
                ON CONFLICT DO NOTHING;
                UPDATE leads SET
                  "StageId" = CASE "Status" WHEN 2 THEN '0193a000-0000-7000-8000-000000000002'::uuid
                                            WHEN 3 THEN '0193a000-0000-7000-8000-000000000006'::uuid
                                            WHEN 4 THEN '0193a000-0000-7000-8000-000000000007'::uuid
                                            ELSE '0193a000-0000-7000-8000-000000000001'::uuid END,
                  "ClosedAt" = CASE WHEN "Status" IN (3, 4) THEN COALESCE("UpdatedAt", "CreatedAt") END,
                  "LastActivityAt" = COALESCE("UpdatedAt", "CreatedAt"),
                  "SourceId" = '0193a000-0000-7000-8000-000000000104',
                  "Tags" = '[]';
                -- Roles that managed website leads now manage the CRM.
                INSERT INTO role_permissions ("RoleId", "Code", "Scope")
                  SELECT "RoleId", c, 3 FROM role_permissions, unnest(ARRAY['crm.assign', 'crm.pipeline.manage']) AS c WHERE "Code" = 'leads.manage'
                ON CONFLICT DO NOTHING;
                UPDATE role_permissions SET "Code" = 'crm.leads' WHERE "Code" = 'leads.manage';
                """);

            migrationBuilder.AddForeignKey(
                name: "FK_leads_lead_sources_SourceId",
                table: "leads",
                column: "SourceId",
                principalTable: "lead_sources",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_leads_lead_stages_StageId",
                table: "leads",
                column: "StageId",
                principalTable: "lead_stages",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.DropColumn(
                name: "Status",
                table: "leads");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_leads_lead_sources_SourceId",
                table: "leads");

            migrationBuilder.DropForeignKey(
                name: "FK_leads_lead_stages_StageId",
                table: "leads");

            migrationBuilder.DropTable(
                name: "lead_activities");

            migrationBuilder.DropTable(
                name: "lead_sources");

            migrationBuilder.DropTable(
                name: "lead_stages");

            migrationBuilder.DropIndex(
                name: "IX_leads_MessengerChatId",
                table: "leads");

            migrationBuilder.DropIndex(
                name: "IX_leads_NextFollowUpAt",
                table: "leads");

            migrationBuilder.DropIndex(
                name: "IX_leads_OwnerId_StageId",
                table: "leads");

            migrationBuilder.DropIndex(
                name: "IX_leads_Phone",
                table: "leads");

            migrationBuilder.DropIndex(
                name: "IX_leads_SourceId",
                table: "leads");

            migrationBuilder.DropIndex(
                name: "IX_leads_StageId",
                table: "leads");

            migrationBuilder.DropColumn(
                name: "Budget",
                table: "leads");

            migrationBuilder.DropColumn(
                name: "City",
                table: "leads");

            migrationBuilder.DropColumn(
                name: "ClientId",
                table: "leads");

            migrationBuilder.DropColumn(
                name: "ClosedAt",
                table: "leads");

            migrationBuilder.DropColumn(
                name: "FollowUpRemindedFor",
                table: "leads");

            migrationBuilder.DropColumn(
                name: "Industry",
                table: "leads");

            migrationBuilder.DropColumn(
                name: "Instagram",
                table: "leads");

            migrationBuilder.DropColumn(
                name: "LastActivityAt",
                table: "leads");

            migrationBuilder.DropColumn(
                name: "LostReason",
                table: "leads");

            migrationBuilder.DropColumn(
                name: "MessengerChatId",
                table: "leads");

            migrationBuilder.DropColumn(
                name: "NextFollowUpAt",
                table: "leads");

            migrationBuilder.DropColumn(
                name: "OwnerId",
                table: "leads");

            migrationBuilder.DropColumn(
                name: "SourceId",
                table: "leads");

            migrationBuilder.DropColumn(
                name: "StageId",
                table: "leads");

            migrationBuilder.DropColumn(
                name: "Tags",
                table: "leads");

            migrationBuilder.AlterColumn<string>(
                name: "Source",
                table: "leads",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(200)",
                oldMaxLength: 200,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "Phone",
                table: "leads",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "character varying(20)",
                oldMaxLength: 20,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "Note",
                table: "leads",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(2000)",
                oldMaxLength: 2000,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "Name",
                table: "leads",
                type: "character varying(60)",
                maxLength: 60,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(80)",
                oldMaxLength: 80);

            migrationBuilder.AlterColumn<string>(
                name: "Message",
                table: "leads",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(1000)",
                oldMaxLength: 1000,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "Business",
                table: "leads",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(80)",
                oldMaxLength: 80,
                oldNullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Status",
                table: "leads",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateIndex(
                name: "IX_leads_Phone_CreatedAt",
                table: "leads",
                columns: new[] { "Phone", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_leads_Status",
                table: "leads",
                column: "Status");
        }
    }
}
