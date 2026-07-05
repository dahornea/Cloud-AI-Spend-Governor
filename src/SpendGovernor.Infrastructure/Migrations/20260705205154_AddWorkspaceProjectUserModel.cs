using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SpendGovernor.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddWorkspaceProjectUserModel : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            var defaultUserId = new Guid("11111111-1111-1111-1111-111111111111");
            var defaultWorkspaceId = new Guid("22222222-2222-2222-2222-222222222222");
            var defaultProjectId = new Guid("33333333-3333-3333-3333-333333333333");
            var defaultMemberId = new Guid("44444444-4444-4444-4444-444444444444");
            var defaultDevBudgetId = new Guid("55555555-5555-5555-5555-555555555551");
            var defaultStagingBudgetId = new Guid("55555555-5555-5555-5555-555555555552");
            var defaultProductionBudgetId = new Guid("55555555-5555-5555-5555-555555555553");
            var createdAt = new DateTimeOffset(2026, 7, 5, 0, 0, 0, TimeSpan.Zero);
            const string defaultPolicyYaml = """
                version: 1
                currency: EUR
                defaultRegion: westeurope
                hoursPerMonth: 730
                onInternalError: fail_open

                pullRequests:
                  comment: true
                  checkRun: true

                rules:
                  - id: max-pr-delta
                    description: Block PRs that add more than 250 EUR/month
                    type: monthly_delta
                    threshold: 250
                    action: block
                  - id: warn-pr-delta
                    description: Warn when PRs add more than 100 EUR/month
                    type: monthly_delta
                    threshold: 100
                    action: warn
                  - id: max-unknown-resources
                    description: Warn when too many resources cannot be estimated
                    type: unknown_resource_count
                    threshold: 3
                    action: warn

                environments:
                  dev:
                    monthlyBudget: 200
                    action: warn
                  staging:
                    monthlyBudget: 500
                    action: approval_required
                  production:
                    monthlyBudget: 3000
                    action: block

                ai:
                  enabled: true
                  monthlyBudget: 300
                  maxCostPerWorkflowMonthly: 100
                  action: warn
                """;

            migrationBuilder.DropIndex(
                name: "IX_Repositories_Provider_FullName",
                table: "Repositories");

            migrationBuilder.AddColumn<Guid>(
                name: "ProjectId",
                table: "Repositories",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: defaultProjectId);

            migrationBuilder.CreateTable(
                name: "ApplicationUsers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Email = table.Column<string>(type: "nvarchar(320)", maxLength: 320, nullable: false),
                    UserName = table.Column<string>(type: "nvarchar(320)", maxLength: 320, nullable: false),
                    DisplayName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    PasswordHash = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    LastLoginAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ApplicationUsers", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Workspaces",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Slug = table.Column<string>(type: "nvarchar(220)", maxLength: 220, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Workspaces", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Workspaces_ApplicationUsers_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalTable: "ApplicationUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Projects",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Slug = table.Column<string>(type: "nvarchar(220)", maxLength: 220, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    Provider = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Currency = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    DefaultRegion = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    HoursPerMonth = table.Column<int>(type: "int", nullable: false),
                    PolicyYaml = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Projects", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Projects_Workspaces_WorkspaceId",
                        column: x => x.WorkspaceId,
                        principalTable: "Workspaces",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "WorkspaceMembers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Role = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkspaceMembers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WorkspaceMembers_ApplicationUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "ApplicationUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_WorkspaceMembers_Workspaces_WorkspaceId",
                        column: x => x.WorkspaceId,
                        principalTable: "Workspaces",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "EnvironmentBudgets",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Environment = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    MaxMonthlyCost = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    MaxMonthlyDelta = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    Currency = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    RequireApprovalAbove = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    BlockOnBudgetExceeded = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EnvironmentBudgets", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EnvironmentBudgets_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.InsertData(
                table: "ApplicationUsers",
                columns: new[] { "Id", "Email", "UserName", "DisplayName", "PasswordHash", "CreatedAt", "UpdatedAt", "LastLoginAt" },
                values: new object[] { defaultUserId, "demo@spendgov.local", "demo@spendgov.local", "Demo Owner", null, createdAt, createdAt, null });

            migrationBuilder.InsertData(
                table: "Workspaces",
                columns: new[] { "Id", "Name", "Slug", "CreatedAt", "UpdatedAt", "CreatedByUserId" },
                values: new object[] { defaultWorkspaceId, "Default Workspace", "default-workspace", createdAt, createdAt, defaultUserId });

            migrationBuilder.InsertData(
                table: "Projects",
                columns: new[] { "Id", "WorkspaceId", "Name", "Slug", "Description", "Provider", "Currency", "DefaultRegion", "HoursPerMonth", "PolicyYaml", "CreatedAt", "UpdatedAt" },
                values: new object[] { defaultProjectId, defaultWorkspaceId, "Default Project", "default-project", null, "azure", "EUR", "westeurope", 730, defaultPolicyYaml, createdAt, createdAt });

            migrationBuilder.InsertData(
                table: "WorkspaceMembers",
                columns: new[] { "Id", "WorkspaceId", "UserId", "Role", "CreatedAt", "UpdatedAt" },
                values: new object[] { defaultMemberId, defaultWorkspaceId, defaultUserId, "Owner", createdAt, createdAt });

            migrationBuilder.InsertData(
                table: "EnvironmentBudgets",
                columns: new[] { "Id", "ProjectId", "Environment", "MaxMonthlyCost", "MaxMonthlyDelta", "Currency", "RequireApprovalAbove", "BlockOnBudgetExceeded", "CreatedAt", "UpdatedAt" },
                values: new object[,]
                {
                    { defaultDevBudgetId, defaultProjectId, "dev", 100m, 100m, "EUR", null, false, createdAt, createdAt },
                    { defaultStagingBudgetId, defaultProjectId, "staging", 250m, 250m, "EUR", 250m, false, createdAt, createdAt },
                    { defaultProductionBudgetId, defaultProjectId, "production", 1000m, 250m, "EUR", null, true, createdAt, createdAt }
                });

            migrationBuilder.CreateIndex(
                name: "IX_Repositories_InstallationId",
                table: "Repositories",
                column: "InstallationId");

            migrationBuilder.CreateIndex(
                name: "IX_Repositories_ProjectId",
                table: "Repositories",
                column: "ProjectId");

            migrationBuilder.CreateIndex(
                name: "IX_Repositories_ProjectId_Provider_FullName",
                table: "Repositories",
                columns: new[] { "ProjectId", "Provider", "FullName" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Repositories_Provider_FullName",
                table: "Repositories",
                columns: new[] { "Provider", "FullName" });

            migrationBuilder.CreateIndex(
                name: "IX_ApplicationUsers_Email",
                table: "ApplicationUsers",
                column: "Email",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ApplicationUsers_UserName",
                table: "ApplicationUsers",
                column: "UserName");

            migrationBuilder.CreateIndex(
                name: "IX_EnvironmentBudgets_ProjectId_Environment",
                table: "EnvironmentBudgets",
                columns: new[] { "ProjectId", "Environment" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Projects_WorkspaceId",
                table: "Projects",
                column: "WorkspaceId");

            migrationBuilder.CreateIndex(
                name: "IX_Projects_WorkspaceId_Slug",
                table: "Projects",
                columns: new[] { "WorkspaceId", "Slug" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WorkspaceMembers_Role",
                table: "WorkspaceMembers",
                column: "Role");

            migrationBuilder.CreateIndex(
                name: "IX_WorkspaceMembers_UserId",
                table: "WorkspaceMembers",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_WorkspaceMembers_WorkspaceId",
                table: "WorkspaceMembers",
                column: "WorkspaceId");

            migrationBuilder.CreateIndex(
                name: "IX_WorkspaceMembers_WorkspaceId_UserId",
                table: "WorkspaceMembers",
                columns: new[] { "WorkspaceId", "UserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Workspaces_CreatedByUserId",
                table: "Workspaces",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_Workspaces_Slug",
                table: "Workspaces",
                column: "Slug",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Repositories_Projects_ProjectId",
                table: "Repositories",
                column: "ProjectId",
                principalTable: "Projects",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Repositories_Projects_ProjectId",
                table: "Repositories");

            migrationBuilder.DropTable(
                name: "EnvironmentBudgets");

            migrationBuilder.DropTable(
                name: "WorkspaceMembers");

            migrationBuilder.DropTable(
                name: "Projects");

            migrationBuilder.DropTable(
                name: "Workspaces");

            migrationBuilder.DropTable(
                name: "ApplicationUsers");

            migrationBuilder.DropIndex(
                name: "IX_Repositories_InstallationId",
                table: "Repositories");

            migrationBuilder.DropIndex(
                name: "IX_Repositories_ProjectId",
                table: "Repositories");

            migrationBuilder.DropIndex(
                name: "IX_Repositories_ProjectId_Provider_FullName",
                table: "Repositories");

            migrationBuilder.DropIndex(
                name: "IX_Repositories_Provider_FullName",
                table: "Repositories");

            migrationBuilder.DropColumn(
                name: "ProjectId",
                table: "Repositories");

            migrationBuilder.CreateIndex(
                name: "IX_Repositories_Provider_FullName",
                table: "Repositories",
                columns: new[] { "Provider", "FullName" },
                unique: true);
        }
    }
}
