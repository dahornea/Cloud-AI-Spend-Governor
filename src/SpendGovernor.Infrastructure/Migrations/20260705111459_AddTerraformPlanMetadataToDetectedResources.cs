using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SpendGovernor.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddTerraformPlanMetadataToDetectedResources : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "TerraformActions",
                table: "DetectedResources",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TerraformAddress",
                table: "DetectedResources",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AfterSummary",
                table: "CostBreakdownItems",
                type: "nvarchar(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BeforeSummary",
                table: "CostBreakdownItems",
                type: "nvarchar(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TerraformActions",
                table: "CostBreakdownItems",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TerraformAddress",
                table: "CostBreakdownItems",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "TerraformActions",
                table: "DetectedResources");

            migrationBuilder.DropColumn(
                name: "TerraformAddress",
                table: "DetectedResources");

            migrationBuilder.DropColumn(
                name: "AfterSummary",
                table: "CostBreakdownItems");

            migrationBuilder.DropColumn(
                name: "BeforeSummary",
                table: "CostBreakdownItems");

            migrationBuilder.DropColumn(
                name: "TerraformActions",
                table: "CostBreakdownItems");

            migrationBuilder.DropColumn(
                name: "TerraformAddress",
                table: "CostBreakdownItems");
        }
    }
}
