using Microsoft.EntityFrameworkCore.Migrations;

namespace Cleanuparr.Persistence.Migrations.Events;

public partial class AddUsenetEvents : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(name: "usenet_observations", columns: table => new
            {
                id = table.Column<string>(type: "TEXT", nullable: false),
                client_id = table.Column<Guid>(type: "TEXT", nullable: false),
                owner_id = table.Column<Guid>(type: "TEXT", nullable: false),
                last_seen_ticks = table.Column<long>(type: "INTEGER", nullable: false),
                unchanged_since_ticks = table.Column<long>(type: "INTEGER", nullable: false),
                samples = table.Column<int>(type: "INTEGER", nullable: false),
                fingerprint = table.Column<string>(type: "TEXT", nullable: false),
                progress = table.Column<string>(type: "TEXT", nullable: false),
                incident = table.Column<string>(type: "TEXT", nullable: false),
                action_state = table.Column<string>(type: "TEXT", nullable: false),
                action_ticks = table.Column<long>(type: "INTEGER", nullable: false),
                recovery_progress = table.Column<string>(type: "TEXT", nullable: false)
            }, constraints: table => table.PrimaryKey("pk_usenet_observations", x => x.id));
    }
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "usenet_observations");
    }
}
