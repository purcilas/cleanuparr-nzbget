using Microsoft.EntityFrameworkCore.Migrations;

namespace Cleanuparr.Persistence.Migrations.Data;

public partial class AddUsenetData : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(name: "usenet_options_json", table: "download_clients", type: "TEXT", nullable: true);
    }
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "usenet_options_json", table: "download_clients");
    }
}
