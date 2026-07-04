using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SpendGovernor.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class GitHubPublishingMetadata : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "GitHubCheckRunId",
                table: "PullRequestScans",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "GitHubReportUrl",
                table: "PullRequestScans",
                type: "nvarchar(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReportPublishingError",
                table: "PullRequestScans",
                type: "nvarchar(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReportPublishingStatus",
                table: "PullRequestScans",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "Pending");

            migrationBuilder.CreateIndex(
                name: "IX_PullRequestScans_GitHubCheckRunId",
                table: "PullRequestScans",
                column: "GitHubCheckRunId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_PullRequestScans_GitHubCheckRunId",
                table: "PullRequestScans");

            migrationBuilder.DropColumn(
                name: "GitHubCheckRunId",
                table: "PullRequestScans");

            migrationBuilder.DropColumn(
                name: "GitHubReportUrl",
                table: "PullRequestScans");

            migrationBuilder.DropColumn(
                name: "ReportPublishingError",
                table: "PullRequestScans");

            migrationBuilder.DropColumn(
                name: "ReportPublishingStatus",
                table: "PullRequestScans");
        }
    }
}
