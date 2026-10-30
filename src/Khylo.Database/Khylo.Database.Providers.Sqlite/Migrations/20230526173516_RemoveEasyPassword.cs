using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Khylo.Server.Implementations.Migrations
{
    /// <inheritdoc />
    public partial class RemoveEasyPassword : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "EasyPassword",
                schema: "khylo",
                table: "Users");

            migrationBuilder.RenameTable(
                name: "Users",
                schema: "khylo",
                newName: "Users");

            migrationBuilder.RenameTable(
                name: "Preferences",
                schema: "khylo",
                newName: "Preferences");

            migrationBuilder.RenameTable(
                name: "Permissions",
                schema: "khylo",
                newName: "Permissions");

            migrationBuilder.RenameTable(
                name: "ItemDisplayPreferences",
                schema: "khylo",
                newName: "ItemDisplayPreferences");

            migrationBuilder.RenameTable(
                name: "ImageInfos",
                schema: "khylo",
                newName: "ImageInfos");

            migrationBuilder.RenameTable(
                name: "HomeSection",
                schema: "khylo",
                newName: "HomeSection");

            migrationBuilder.RenameTable(
                name: "DisplayPreferences",
                schema: "khylo",
                newName: "DisplayPreferences");

            migrationBuilder.RenameTable(
                name: "Devices",
                schema: "khylo",
                newName: "Devices");

            migrationBuilder.RenameTable(
                name: "DeviceOptions",
                schema: "khylo",
                newName: "DeviceOptions");

            migrationBuilder.RenameTable(
                name: "CustomItemDisplayPreferences",
                schema: "khylo",
                newName: "CustomItemDisplayPreferences");

            migrationBuilder.RenameTable(
                name: "ApiKeys",
                schema: "khylo",
                newName: "ApiKeys");

            migrationBuilder.RenameTable(
                name: "ActivityLogs",
                schema: "khylo",
                newName: "ActivityLogs");

            migrationBuilder.RenameTable(
                name: "AccessSchedules",
                schema: "khylo",
                newName: "AccessSchedules");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "khylo");

            migrationBuilder.RenameTable(
                name: "Users",
                newName: "Users",
                newSchema: "khylo");

            migrationBuilder.RenameTable(
                name: "Preferences",
                newName: "Preferences",
                newSchema: "khylo");

            migrationBuilder.RenameTable(
                name: "Permissions",
                newName: "Permissions",
                newSchema: "khylo");

            migrationBuilder.RenameTable(
                name: "ItemDisplayPreferences",
                newName: "ItemDisplayPreferences",
                newSchema: "khylo");

            migrationBuilder.RenameTable(
                name: "ImageInfos",
                newName: "ImageInfos",
                newSchema: "khylo");

            migrationBuilder.RenameTable(
                name: "HomeSection",
                newName: "HomeSection",
                newSchema: "khylo");

            migrationBuilder.RenameTable(
                name: "DisplayPreferences",
                newName: "DisplayPreferences",
                newSchema: "khylo");

            migrationBuilder.RenameTable(
                name: "Devices",
                newName: "Devices",
                newSchema: "khylo");

            migrationBuilder.RenameTable(
                name: "DeviceOptions",
                newName: "DeviceOptions",
                newSchema: "khylo");

            migrationBuilder.RenameTable(
                name: "CustomItemDisplayPreferences",
                newName: "CustomItemDisplayPreferences",
                newSchema: "khylo");

            migrationBuilder.RenameTable(
                name: "ApiKeys",
                newName: "ApiKeys",
                newSchema: "khylo");

            migrationBuilder.RenameTable(
                name: "ActivityLogs",
                newName: "ActivityLogs",
                newSchema: "khylo");

            migrationBuilder.RenameTable(
                name: "AccessSchedules",
                newName: "AccessSchedules",
                newSchema: "khylo");

            migrationBuilder.AddColumn<string>(
                name: "EasyPassword",
                schema: "khylo",
                table: "Users",
                type: "TEXT",
                maxLength: 65535,
                nullable: true);
        }
    }
}
