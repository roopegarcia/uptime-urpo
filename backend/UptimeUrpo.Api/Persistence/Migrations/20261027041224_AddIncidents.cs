using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace UptimeUrpo.Api.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddIncidents : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "FirstFailureAtUtc",
                table: "monitored_services",
                type: "timestamp with time zone",
                nullable: true);

            // M3 kept only the latest failure snapshot. Seed that known time, not
            // an invented earlier incident; new tracked streaks retain their true first failure.
            migrationBuilder.Sql("""
                UPDATE monitored_services SET "FirstFailureAtUtc" = "LastCheckedAtUtc"
                WHERE "LastSuccess" = false AND "ConsecutiveFailures" > 0;
                """);

            migrationBuilder.CreateTable(
                name: "incidents",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ServiceId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    FirstFailureAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ConfirmedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    EndedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Resolution = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    HasMonitoringGaps = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_incidents", x => x.Id);
                    table.CheckConstraint("CK_Incidents_Resolution", "(\"EndedAtUtc\" IS NULL AND \"Resolution\" IS NULL) OR (\"EndedAtUtc\" IS NOT NULL AND \"Resolution\" IS NOT NULL)");
                    table.ForeignKey(
                        name: "FK_incidents_monitored_services_ServiceId",
                        column: x => x.ServiceId,
                        principalTable: "monitored_services",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_incidents_EndedAtUtc",
                table: "incidents",
                column: "EndedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_incidents_ServiceId",
                table: "incidents",
                column: "ServiceId",
                unique: true,
                filter: "\"EndedAtUtc\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_incidents_ServiceId_Id",
                table: "incidents",
                columns: new[] { "ServiceId", "Id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "incidents");

            migrationBuilder.DropColumn(
                name: "FirstFailureAtUtc",
                table: "monitored_services");
        }
    }
}
