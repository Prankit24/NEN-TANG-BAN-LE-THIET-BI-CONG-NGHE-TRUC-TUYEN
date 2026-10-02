using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EMua.Migrations
{
    /// <inheritdoc />
    public partial class AddDeliveryTrackingFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AnhXacNhanGiao",
                table: "DonHang",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "NgayDuKienGiao",
                table: "DonHang",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AnhXacNhanGiao",
                table: "DonHang");

            migrationBuilder.DropColumn(
                name: "NgayDuKienGiao",
                table: "DonHang");
        }
    }
}
