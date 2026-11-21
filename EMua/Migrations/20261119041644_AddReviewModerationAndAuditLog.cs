using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace EMua.Migrations
{
    /// <inheritdoc />
    public partial class AddReviewModerationAndAuditLog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ── Thêm cột kiểm duyệt vào bảng danhgiasanpham ─────────────────
            migrationBuilder.AddColumn<string>(
                name: "ghichukiemduyet",
                schema: "public",
                table: "danhgiasanpham",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "lydoan",
                schema: "public",
                table: "danhgiasanpham",
                type: "character varying(255)",
                maxLength: 255,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "manguoikiemduyet",
                schema: "public",
                table: "danhgiasanpham",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ngaykiemduyet",
                schema: "public",
                table: "danhgiasanpham",
                type: "timestamp without time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "tennguoikiemduyet",
                schema: "public",
                table: "danhgiasanpham",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            // ── Tạo bảng lịch sử kiểm duyệt đánh giá ────────────────────────
            migrationBuilder.CreateTable(
                name: "lichsukiemduyetdanhgia",
                schema: "public",
                columns: table => new
                {
                    malichsu = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    madanhgia = table.Column<int>(type: "integer", nullable: false),
                    hanhdong = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false, defaultValue: "Ẩn"),
                    lydovipham = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false, defaultValue: ""),
                    ghichuchitiet = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    manguoikiemduyet = table.Column<int>(type: "integer", nullable: true),
                    tennguoikiemduyet = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    ngaythuchien = table.Column<DateTime>(type: "timestamp without time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_lichsukiemduyetdanhgia", x => x.malichsu);
                    table.ForeignKey(
                        name: "FK_LichSuKiemDuyet_DanhGia",
                        column: x => x.madanhgia,
                        principalSchema: "public",
                        principalTable: "danhgiasanpham",
                        principalColumn: "madanhgia",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_LichSuKiemDuyet_NguoiDung",
                        column: x => x.manguoikiemduyet,
                        principalSchema: "public",
                        principalTable: "nguoidung",
                        principalColumn: "manguoidung",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_lichsukiemduyetdanhgia_madanhgia",
                schema: "public",
                table: "lichsukiemduyetdanhgia",
                column: "madanhgia");

            migrationBuilder.CreateIndex(
                name: "IX_lichsukiemduyetdanhgia_manguoikiemduyet",
                schema: "public",
                table: "lichsukiemduyetdanhgia",
                column: "manguoikiemduyet");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "lichsukiemduyetdanhgia",
                schema: "public");

            migrationBuilder.DropColumn(
                name: "ghichukiemduyet",
                schema: "public",
                table: "danhgiasanpham");

            migrationBuilder.DropColumn(
                name: "lydoan",
                schema: "public",
                table: "danhgiasanpham");

            migrationBuilder.DropColumn(
                name: "manguoikiemduyet",
                schema: "public",
                table: "danhgiasanpham");

            migrationBuilder.DropColumn(
                name: "ngaykiemduyet",
                schema: "public",
                table: "danhgiasanpham");

            migrationBuilder.DropColumn(
                name: "tennguoikiemduyet",
                schema: "public",
                table: "danhgiasanpham");
        }
    }
}
