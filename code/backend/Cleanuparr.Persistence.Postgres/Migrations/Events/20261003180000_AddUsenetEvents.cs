using Microsoft.EntityFrameworkCore.Migrations;

namespace Cleanuparr.Persistence.Postgres.Migrations.Events;

public partial class AddUsenetEvents : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(name: "usenet_observations", schema: "events", columns: table => new
            {
                id = table.Column<string>(type: "text", nullable: false),
                client_id = table.Column<Guid>(type: "uuid", nullable: false),
                owner_id = table.Column<Guid>(type: "uuid", nullable: false),
                last_seen_ticks = table.Column<long>(type: "bigint", nullable: false),
                unchanged_since_ticks = table.Column<long>(type: "bigint", nullable: false),
                samples = table.Column<int>(type: "integer", nullable: false),
                fingerprint = table.Column<string>(type: "text", nullable: false),
                progress = table.Column<string>(type: "text", nullable: false),
                incident = table.Column<string>(type: "text", nullable: false),
                action_state = table.Column<string>(type: "text", nullable: false),
                action_ticks = table.Column<long>(type: "bigint", nullable: false),
                recovery_progress = table.Column<string>(type: "text", nullable: false)
            }, constraints: table => table.PrimaryKey("pk_usenet_observations", x => x.id));
    }
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "usenet_observations", schema: "events");
    }
}
