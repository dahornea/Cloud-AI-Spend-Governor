using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SpendGovernor.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPricingMetadataToCostBreakdown : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "PricingCatalogVersion",
                table: "CostBreakdownItems",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PricingFallbackReason",
                table: "CostBreakdownItems",
                type: "nvarchar(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PricingMatchType",
                table: "CostBreakdownItems",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PricingSource",
                table: "CostBreakdownItems",
                type: "nvarchar(300)",
                maxLength: 300,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PricingCatalogVersion",
                table: "CostBreakdownItems");

            migrationBuilder.DropColumn(
                name: "PricingFallbackReason",
                table: "CostBreakdownItems");

            migrationBuilder.DropColumn(
                name: "PricingMatchType",
                table: "CostBreakdownItems");

            migrationBuilder.DropColumn(
                name: "PricingSource",
                table: "CostBreakdownItems");
        }
    }
}
