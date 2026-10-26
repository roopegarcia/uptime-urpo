using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace UptimeUrpo.Api.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialMonitoring : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "monitored_services",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Name = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    Url = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    Enabled = table.Column<bool>(type: "boolean", nullable: false),
                    Retired = table.Column<bool>(type: "boolean", nullable: false),
                    IntervalSeconds = table.Column<int>(type: "integer", nullable: false),
                    TimeoutSeconds = table.Column<int>(type: "integer", nullable: false),
                    ExpectedStatusCode = table.Column<int>(type: "integer", nullable: false),
                    FailureThreshold = table.Column<int>(type: "integer", nullable: false),
                    SlowResponseThresholdMs = table.Column<int>(type: "integer", nullable: false),
                    RegisteredAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ConsecutiveFailures = table.Column<int>(type: "integer", nullable: false),
                    LastCheckedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LastCompletedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LastDurationMs = table.Column<double>(type: "double precision", nullable: true),
                    LastStatusCode = table.Column<int>(type: "integer", nullable: true),
                    LastSuccess = table.Column<bool>(type: "boolean", nullable: true),
                    LastFailureCategory = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_monitored_services", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "monitoring_periods",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ServiceId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    StartedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    EndedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Url = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    Enabled = table.Column<bool>(type: "boolean", nullable: false),
                    IntervalSeconds = table.Column<int>(type: "integer", nullable: false),
                    TimeoutSeconds = table.Column<int>(type: "integer", nullable: false),
                    ExpectedStatusCode = table.Column<int>(type: "integer", nullable: false),
                    FailureThreshold = table.Column<int>(type: "integer", nullable: false),
                    SlowResponseThresholdMs = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_monitoring_periods", x => x.Id);
                    table.ForeignKey(
                        name: "FK_monitoring_periods_monitored_services_ServiceId",
                        column: x => x.ServiceId,
                        principalTable: "monitored_services",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "check_results",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ServiceId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    MonitoringPeriodId = table.Column<long>(type: "bigint", nullable: false),
                    CheckedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CompletedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    DurationMs = table.Column<double>(type: "double precision", nullable: false),
                    StatusCode = table.Column<int>(type: "integer", nullable: true),
                    Success = table.Column<bool>(type: "boolean", nullable: false),
                    FailureCategory = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_check_results", x => x.Id);
                    table.CheckConstraint("CK_CheckResults_Duration", "\"DurationMs\" >= 0");
                    table.ForeignKey(
                        name: "FK_check_results_monitored_services_ServiceId",
                        column: x => x.ServiceId,
                        principalTable: "monitored_services",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_check_results_monitoring_periods_MonitoringPeriodId",
                        column: x => x.MonitoringPeriodId,
                        principalTable: "monitoring_periods",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_check_results_CompletedAtUtc",
                table: "check_results",
                column: "CompletedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_check_results_MonitoringPeriodId",
                table: "check_results",
                column: "MonitoringPeriodId");

            migrationBuilder.CreateIndex(
                name: "IX_check_results_ServiceId_CheckedAtUtc",
                table: "check_results",
                columns: new[] { "ServiceId", "CheckedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_monitoring_periods_ServiceId",
                table: "monitoring_periods",
                column: "ServiceId",
                unique: true,
                filter: "\"EndedAtUtc\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_monitoring_periods_ServiceId_StartedAtUtc",
                table: "monitoring_periods",
                columns: new[] { "ServiceId", "StartedAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "check_results");

            migrationBuilder.DropTable(
                name: "monitoring_periods");

            migrationBuilder.DropTable(
                name: "monitored_services");
        }
    }
}
