using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace EMua.Migrations
{
    /// <inheritdoc />
    public partial class AddTinNhanDonHangTable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "TinNhanDonHang",
                columns: table => new
                {
                    MaTinNhan = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    MaDonHang = table.Column<int>(type: "integer", nullable: false),
                    MaNguoiGui = table.Column<int>(type: "integer", nullable: true),
                    IsStaff = table.Column<bool>(type: "boolean", nullable: false),
                    NoiDung = table.Column<string>(type: "text", nullable: false),
                    IsRead = table.Column<bool>(type: "boolean", nullable: false),
                    ThoiGian = table.Column<DateTime>(type: "timestamp without time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TinNhanDonHang", x => x.MaTinNhan);
                    table.ForeignKey(
                        name: "FK_TinNhanDonHang_DonHang_MaDonHang",
                        column: x => x.MaDonHang,
                        principalTable: "DonHang",
                        principalColumn: "MaDonHang",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_TinNhanDonHang_NguoiDung_MaNguoiGui",
                        column: x => x.MaNguoiGui,
                        principalTable: "NguoiDung",
                        principalColumn: "MaNguoiDung");
                });

            migrationBuilder.CreateIndex(
                name: "IX_TinNhanDonHang_MaDonHang",
                table: "TinNhanDonHang",
                column: "MaDonHang");

            migrationBuilder.CreateIndex(
                name: "IX_TinNhanDonHang_MaNguoiGui",
                table: "TinNhanDonHang",
                column: "MaNguoiGui");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TinNhanDonHang");
        }
    }
}
