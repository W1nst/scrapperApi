using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TourSearch.Migrations
{
    /// <inheritdoc />
    public partial class InitialSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "tour_search");

            migrationBuilder.CreateTable(
                name: "regions",
                schema: "tour_search",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false),
                    label = table.Column<string>(type: "text", nullable: false),
                    source = table.Column<string>(type: "text", nullable: false),
                    active = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_regions", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "search_jobs",
                schema: "tour_search",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    parameters_hash = table.Column<string>(type: "character varying(71)", maxLength: 71, nullable: false),
                    currency_code = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    attempt = table.Column<int>(type: "integer", nullable: false),
                    lease_token = table.Column<string>(type: "text", nullable: true),
                    lease_expires_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    cancel_requested_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    started_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    completed_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    degraded = table.Column<bool>(type: "boolean", nullable: false),
                    warnings = table.Column<List<string>>(type: "text[]", nullable: false),
                    offers_count = table.Column<int>(type: "integer", nullable: false),
                    pages_done = table.Column<int>(type: "integer", nullable: false),
                    pages_total = table.Column<int>(type: "integer", nullable: true),
                    error_code = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    error_message = table.Column<string>(type: "text", nullable: true),
                    retryable = table.Column<bool>(type: "boolean", nullable: true),
                    parameters = table.Column<string>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_search_jobs", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "tour_offers",
                schema: "tour_search",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    search_job_id = table.Column<Guid>(type: "uuid", nullable: false),
                    hotel_name = table.Column<string>(type: "text", nullable: false),
                    hotel_stars = table.Column<int>(type: "integer", nullable: false),
                    luxury = table.Column<bool>(type: "boolean", nullable: false),
                    region_id = table.Column<string>(type: "text", nullable: false),
                    check_in = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    nights = table.Column<int>(type: "integer", nullable: false),
                    price = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    currency_code = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    room_type = table.Column<string>(type: "text", nullable: true),
                    meal_type = table.Column<string>(type: "text", nullable: true),
                    source_url = table.Column<string>(type: "text", nullable: true),
                    scraped_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_tour_offers", x => x.id);
                    table.ForeignKey(
                        name: "fk_tour_offers_regions_region_id",
                        column: x => x.region_id,
                        principalSchema: "tour_search",
                        principalTable: "regions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_tour_offers_search_jobs_search_job_id",
                        column: x => x.search_job_id,
                        principalSchema: "tour_search",
                        principalTable: "search_jobs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_search_jobs_parameters_hash",
                schema: "tour_search",
                table: "search_jobs",
                column: "parameters_hash");

            migrationBuilder.CreateIndex(
                name: "ix_search_jobs_status_created_at",
                schema: "tour_search",
                table: "search_jobs",
                columns: new[] { "status", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_search_jobs_status_lease_expires_at",
                schema: "tour_search",
                table: "search_jobs",
                columns: new[] { "status", "lease_expires_at" });

            migrationBuilder.CreateIndex(
                name: "ix_tour_offers_check_in",
                schema: "tour_search",
                table: "tour_offers",
                column: "check_in");

            migrationBuilder.CreateIndex(
                name: "ix_tour_offers_price",
                schema: "tour_search",
                table: "tour_offers",
                column: "price");

            migrationBuilder.CreateIndex(
                name: "ix_tour_offers_region_id",
                schema: "tour_search",
                table: "tour_offers",
                column: "region_id");

            migrationBuilder.CreateIndex(
                name: "ix_tour_offers_scraped_at_id",
                schema: "tour_search",
                table: "tour_offers",
                columns: new[] { "scraped_at", "id" });

            migrationBuilder.CreateIndex(
                name: "ix_tour_offers_search_job_id",
                schema: "tour_search",
                table: "tour_offers",
                column: "search_job_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "tour_offers",
                schema: "tour_search");

            migrationBuilder.DropTable(
                name: "regions",
                schema: "tour_search");

            migrationBuilder.DropTable(
                name: "search_jobs",
                schema: "tour_search");
        }
    }
}
