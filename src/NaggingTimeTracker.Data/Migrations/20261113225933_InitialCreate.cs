using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace NaggingTimeTracker.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "customers",
                columns: table => new
                {
                    id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    name = table.Column<string>(type: "TEXT", nullable: false, collation: "NOCASE"),
                    archived = table.Column<bool>(type: "INTEGER", nullable: false),
                    created_utc = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_customers", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "reminders",
                columns: table => new
                {
                    id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    title = table.Column<string>(type: "TEXT", nullable: false),
                    message = table.Column<string>(type: "TEXT", nullable: false),
                    days_mask = table.Column<int>(type: "INTEGER", nullable: false),
                    time_local = table.Column<string>(type: "TEXT", nullable: false),
                    enabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    last_fired_utc = table.Column<long>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_reminders", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "settings",
                columns: table => new
                {
                    key = table.Column<string>(type: "TEXT", nullable: false),
                    value = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_settings", x => x.key);
                });

            migrationBuilder.CreateTable(
                name: "task_types",
                columns: table => new
                {
                    id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    name = table.Column<string>(type: "TEXT", nullable: false, collation: "NOCASE"),
                    archived = table.Column<bool>(type: "INTEGER", nullable: false),
                    created_utc = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_task_types", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "entries",
                columns: table => new
                {
                    id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    customer_id = table.Column<long>(type: "INTEGER", nullable: false),
                    task_type_id = table.Column<long>(type: "INTEGER", nullable: false),
                    start_utc = table.Column<long>(type: "INTEGER", nullable: false),
                    stop_utc = table.Column<long>(type: "INTEGER", nullable: true),
                    external_link = table.Column<string>(type: "TEXT", nullable: true),
                    notes = table.Column<string>(type: "TEXT", nullable: false),
                    continued_from_id = table.Column<long>(type: "INTEGER", nullable: true),
                    overlap_reason = table.Column<string>(type: "TEXT", nullable: true),
                    created_utc = table.Column<long>(type: "INTEGER", nullable: false),
                    updated_utc = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_entries", x => x.id);
                    table.CheckConstraint("ck_entries_stop_after_start", "stop_utc IS NULL OR stop_utc > start_utc");
                    table.ForeignKey(
                        name: "FK_entries_customers_customer_id",
                        column: x => x.customer_id,
                        principalTable: "customers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_entries_entries_continued_from_id",
                        column: x => x.continued_from_id,
                        principalTable: "entries",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_entries_task_types_task_type_id",
                        column: x => x.task_type_id,
                        principalTable: "task_types",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.InsertData(
                table: "task_types",
                columns: new[] { "id", "archived", "created_utc", "name" },
                values: new object[,]
                {
                    { 1L, false, 1767225600000L, "Admin" },
                    { 2L, false, 1767225600000L, "Training" },
                    { 3L, false, 1767225600000L, "Dev Work" },
                    { 4L, false, 1767225600000L, "Meetings" },
                    { 5L, false, 1767225600000L, "Support" }
                });

            migrationBuilder.CreateIndex(
                name: "ux_customers_name",
                table: "customers",
                column: "name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_entries_continued_from_id",
                table: "entries",
                column: "continued_from_id");

            migrationBuilder.CreateIndex(
                name: "IX_entries_customer_id",
                table: "entries",
                column: "customer_id");

            migrationBuilder.CreateIndex(
                name: "ix_entries_running",
                table: "entries",
                column: "stop_utc",
                filter: "stop_utc IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_entries_start",
                table: "entries",
                column: "start_utc");

            migrationBuilder.CreateIndex(
                name: "IX_entries_task_type_id",
                table: "entries",
                column: "task_type_id");

            migrationBuilder.CreateIndex(
                name: "ux_task_types_name",
                table: "task_types",
                column: "name",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "entries");

            migrationBuilder.DropTable(
                name: "reminders");

            migrationBuilder.DropTable(
                name: "settings");

            migrationBuilder.DropTable(
                name: "customers");

            migrationBuilder.DropTable(
                name: "task_types");
        }
    }
}
