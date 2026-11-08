using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace EMua.Migrations
{
    /// <inheritdoc />
    public partial class AddPhieuNhapHangTables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PhieuNhapHang",
                columns: table => new
                {
                    MaPhieuNhap = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    MaChungTu = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    NhaCungCap = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    LyDoNhap = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    DienGiai = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    TrangThai = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false, defaultValue: "Chờ duyệt"),
                    MaNguoiTao = table.Column<int>(type: "integer", nullable: false),
                    NgayTao = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    MaNguoiDuyet = table.Column<int>(type: "integer", nullable: true),
                    NgayDuyet = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    LyDoTuChoi = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    GhiChu = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    TongTienDuKien = table.Column<decimal>(type: "numeric(18,2)", nullable: false, defaultValue: 0m),
                    NgayNhapKho = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    MaNguoiNhapKho = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PhieuNhapHang", x => x.MaPhieuNhap);
                    table.ForeignKey(
                        name: "FK_PhieuNhapHang_NguoiDung_MaNguoiTao",
                        column: x => x.MaNguoiTao,
                        principalTable: "nguoidung",
                        principalColumn: "manguoidung",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PhieuNhapHang_NguoiDung_MaNguoiDuyet",
                        column: x => x.MaNguoiDuyet,
                        principalTable: "nguoidung",
                        principalColumn: "manguoidung",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PhieuNhapHang_NguoiDung_MaNguoiNhapKho",
                        column: x => x.MaNguoiNhapKho,
                        principalTable: "nguoidung",
                        principalColumn: "manguoidung",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ChiTietPhieuNhapHang",
                columns: table => new
                {
                    MaChiTietPhieuNhap = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    MaPhieuNhap = table.Column<int>(type: "integer", nullable: false),
                    MaBienThe = table.Column<int>(type: "integer", nullable: false),
                    SoLuongNhap = table.Column<int>(type: "integer", nullable: false),
                    DonGiaNhapDuKien = table.Column<decimal>(type: "numeric(18,2)", nullable: false, defaultValue: 0m),
                    GhiChu = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChiTietPhieuNhapHang", x => x.MaChiTietPhieuNhap);
                    table.ForeignKey(
                        name: "FK_ChiTietPhieuNhapHang_PhieuNhapHang",
                        column: x => x.MaPhieuNhap,
                        principalTable: "PhieuNhapHang",
                        principalColumn: "MaPhieuNhap",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ChiTietPhieuNhapHang_BienTheSanPham",
                        column: x => x.MaBienThe,
                        principalTable: "bienthesanpham",
                        principalColumn: "mabienthe",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PhieuNhapHang_MaChungTu",
                table: "PhieuNhapHang",
                column: "MaChungTu",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PhieuNhapHang_MaNguoiTao",
                table: "PhieuNhapHang",
                column: "MaNguoiTao");

            migrationBuilder.CreateIndex(
                name: "IX_PhieuNhapHang_TrangThai",
                table: "PhieuNhapHang",
                column: "TrangThai");

            migrationBuilder.CreateIndex(
                name: "IX_ChiTietPhieuNhapHang_MaPhieuNhap",
                table: "ChiTietPhieuNhapHang",
                column: "MaPhieuNhap");

            migrationBuilder.CreateIndex(
                name: "IX_ChiTietPhieuNhapHang_MaBienThe",
                table: "ChiTietPhieuNhapHang",
                column: "MaBienThe");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ChiTietPhieuNhapHang");

            migrationBuilder.DropTable(
                name: "PhieuNhapHang");
        }
    }
}
