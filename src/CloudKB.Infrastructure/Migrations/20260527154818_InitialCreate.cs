using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CloudKB.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "index_audit_logs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<string>(type: "text", nullable: false),
                    FileName = table.Column<string>(type: "text", nullable: false),
                    ActionType = table.Column<string>(type: "text", nullable: false),
                    SectionsAffected = table.Column<int>(type: "integer", nullable: false),
                    CommitMessage = table.Column<string>(type: "text", nullable: false),
                    LoggedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_index_audit_logs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "index_compilation_jobs",
                columns: table => new
                {
                    TaskId = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<string>(type: "text", nullable: false),
                    Status = table.Column<string>(type: "text", nullable: false),
                    S3StoragePath = table.Column<string>(type: "text", nullable: false),
                    FileNames = table.Column<string>(type: "jsonb", nullable: false),
                    SectionsCompiled = table.Column<int>(type: "integer", nullable: true),
                    FilesProcessed = table.Column<int>(type: "integer", nullable: true),
                    CompileDurationMs = table.Column<int>(type: "integer", nullable: true),
                    ErrorCode = table.Column<string>(type: "text", nullable: true),
                    ErrorDetail = table.Column<string>(type: "text", nullable: true),
                    RequestedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    StartedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CompletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_index_compilation_jobs", x => x.TaskId);
                });

            migrationBuilder.CreateTable(
                name: "tenant_file_states",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    TenantId = table.Column<string>(type: "text", nullable: false),
                    FileName = table.Column<string>(type: "text", nullable: false),
                    ContentHash = table.Column<string>(type: "text", nullable: false),
                    LastIndexedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tenant_file_states", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "tenant_files",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<string>(type: "text", nullable: false),
                    FileName = table.Column<string>(type: "text", nullable: false),
                    S3Key = table.Column<string>(type: "text", nullable: false),
                    FileSizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    ContentHash = table.Column<string>(type: "text", nullable: false),
                    IsIndexed = table.Column<bool>(type: "boolean", nullable: false),
                    LastIndexedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    UploadedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tenant_files", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "tenant_sections",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    TenantId = table.Column<string>(type: "text", nullable: false),
                    FileName = table.Column<string>(type: "text", nullable: false),
                    Heading = table.Column<string>(type: "text", nullable: false),
                    HeadingPath = table.Column<string>(type: "jsonb", nullable: false),
                    Content = table.Column<string>(type: "text", nullable: false),
                    Tokens = table.Column<string>(type: "jsonb", nullable: false),
                    TokenCount = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tenant_sections", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_index_audit_logs_tenant_logged",
                table: "index_audit_logs",
                columns: new[] { "TenantId", "LoggedAt" });

            migrationBuilder.CreateIndex(
                name: "ix_compilation_jobs_requested_at",
                table: "index_compilation_jobs",
                column: "RequestedAt");

            migrationBuilder.CreateIndex(
                name: "ix_compilation_jobs_status",
                table: "index_compilation_jobs",
                columns: new[] { "TenantId", "Status" });

            migrationBuilder.CreateIndex(
                name: "ix_compilation_jobs_tenant_id",
                table: "index_compilation_jobs",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "ix_tenant_file_states_tenant_id",
                table: "tenant_file_states",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "ix_tenant_files_tenant_id",
                table: "tenant_files",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "uq_tenant_files_tenant_filename",
                table: "tenant_files",
                columns: new[] { "TenantId", "FileName" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_tenant_sections_tenant_file",
                table: "tenant_sections",
                columns: new[] { "TenantId", "FileName" });

            migrationBuilder.CreateIndex(
                name: "ix_tenant_sections_tenant_id",
                table: "tenant_sections",
                column: "TenantId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "index_audit_logs");

            migrationBuilder.DropTable(
                name: "index_compilation_jobs");

            migrationBuilder.DropTable(
                name: "tenant_file_states");

            migrationBuilder.DropTable(
                name: "tenant_files");

            migrationBuilder.DropTable(
                name: "tenant_sections");
        }
    }
}
