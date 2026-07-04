using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SpendGovernor.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialSpendGovernorPersistence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Repositories",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Provider = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Owner = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    FullName = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false),
                    DefaultBranch = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    ExternalRepositoryId = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    InstallationId = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Repositories", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PullRequestScans",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RepositoryId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PullRequestNumber = table.Column<int>(type: "int", nullable: false),
                    SourceBranch = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    TargetBranch = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Environment = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Status = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Decision = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    EstimatedMonthlyDelta = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    Currency = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    ConfidenceLevel = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    StartedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    CompletedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    FailureReason = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    DashboardReportUrl = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    GitHubCommentId = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    GitHubPullRequestUrl = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PullRequestScans", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PullRequestScans_Repositories_RepositoryId",
                        column: x => x.RepositoryId,
                        principalTable: "Repositories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CostBreakdownItems",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PullRequestScanId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ResourceName = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false),
                    ResourceType = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false),
                    ChangeType = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    EstimatedMonthlyCost = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    Currency = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CostBreakdownItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CostBreakdownItems_PullRequestScans_PullRequestScanId",
                        column: x => x.PullRequestScanId,
                        principalTable: "PullRequestScans",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "DetectedResources",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PullRequestScanId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SourceFile = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    Provider = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    ResourceType = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false),
                    ResourceName = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false),
                    Sku = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Region = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    RawJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DetectedResources", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DetectedResources_PullRequestScans_PullRequestScanId",
                        column: x => x.PullRequestScanId,
                        principalTable: "PullRequestScans",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PolicyEvaluations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PullRequestScanId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RuleName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Result = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Message = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PolicyEvaluations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PolicyEvaluations_PullRequestScans_PullRequestScanId",
                        column: x => x.PullRequestScanId,
                        principalTable: "PullRequestScans",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ScanAssumptions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PullRequestScanId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Value = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ScanAssumptions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ScanAssumptions_PullRequestScans_PullRequestScanId",
                        column: x => x.PullRequestScanId,
                        principalTable: "PullRequestScans",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CostBreakdownItems_PullRequestScanId",
                table: "CostBreakdownItems",
                column: "PullRequestScanId");

            migrationBuilder.CreateIndex(
                name: "IX_DetectedResources_PullRequestScanId",
                table: "DetectedResources",
                column: "PullRequestScanId");

            migrationBuilder.CreateIndex(
                name: "IX_PolicyEvaluations_PullRequestScanId",
                table: "PolicyEvaluations",
                column: "PullRequestScanId");

            migrationBuilder.CreateIndex(
                name: "IX_PullRequestScans_CreatedAt",
                table: "PullRequestScans",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_PullRequestScans_Decision",
                table: "PullRequestScans",
                column: "Decision");

            migrationBuilder.CreateIndex(
                name: "IX_PullRequestScans_GitHubCommentId",
                table: "PullRequestScans",
                column: "GitHubCommentId");

            migrationBuilder.CreateIndex(
                name: "IX_PullRequestScans_RepositoryId_PullRequestNumber",
                table: "PullRequestScans",
                columns: new[] { "RepositoryId", "PullRequestNumber" });

            migrationBuilder.CreateIndex(
                name: "IX_PullRequestScans_Status",
                table: "PullRequestScans",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_Repositories_FullName",
                table: "Repositories",
                column: "FullName");

            migrationBuilder.CreateIndex(
                name: "IX_Repositories_Provider_FullName",
                table: "Repositories",
                columns: new[] { "Provider", "FullName" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ScanAssumptions_PullRequestScanId",
                table: "ScanAssumptions",
                column: "PullRequestScanId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CostBreakdownItems");

            migrationBuilder.DropTable(
                name: "DetectedResources");

            migrationBuilder.DropTable(
                name: "PolicyEvaluations");

            migrationBuilder.DropTable(
                name: "ScanAssumptions");

            migrationBuilder.DropTable(
                name: "PullRequestScans");

            migrationBuilder.DropTable(
                name: "Repositories");
        }
    }
}
