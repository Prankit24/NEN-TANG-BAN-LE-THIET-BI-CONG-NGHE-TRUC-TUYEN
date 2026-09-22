using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace EMua.Migrations
{
    public partial class InitialPostgreSql : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "BaoCao",
                columns: table => new
                {
                    MaBaoCao = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    LoaiBaoCao = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    NgayBaoCao = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    DuLieu = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK__BaoCao__25A9188CF7650BAA", x => x.MaBaoCao);
                });

            migrationBuilder.CreateTable(
                name: "DanhMucSanPham",
                columns: table => new
                {
                    MaDanhMuc = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    TenDanhMuc = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    MoTa = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    TrangThaiDanhMuc = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK__DanhMucS__B37508871346CE70", x => x.MaDanhMuc);
                });

            migrationBuilder.CreateTable(
                name: "KhuyenMai",
                columns: table => new
                {
                    MaKhuyenMai = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    MaCode = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    TenKhuyenMai = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    LoaiGiamGia = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    GiaTriGiam = table.Column<decimal>(type: "numeric(12,2)", nullable: false),
                    GiaTriDonHangToiThieu = table.Column<decimal>(type: "numeric(12,2)", nullable: true),
                    SoLuong = table.Column<int>(type: "integer", nullable: true),
                    NgayBatDau = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    NgayKetThuc = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    TrangThai = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    MoTa = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK__KhuyenMa__6F56B3BD8771E9E5", x => x.MaKhuyenMai);
                });

            migrationBuilder.CreateTable(
                name: "PhanQuyen",
                columns: table => new
                {
                    MaQuyen = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    TenQuyen = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    MoTa = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK__PhanQuye__1D4B7ED47D31A58F", x => x.MaQuyen);
                });

            migrationBuilder.CreateTable(
                name: "ThuongHieu",
                columns: table => new
                {
                    MaThuongHieu = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    TenThuongHieu = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Logo = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    MoTa = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    TrangThai = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK__ThuongHi__A3733E2C0EF3AC57", x => x.MaThuongHieu);
                });

            migrationBuilder.CreateTable(
                name: "TuCam",
                columns: table => new
                {
                    MaTuCam = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    NoiDung = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    TrangThai = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    NgayTao = table.Column<DateTime>(type: "timestamp without time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK__TuCam__B89513110632111A", x => x.MaTuCam);
                });

            migrationBuilder.CreateTable(
                name: "NguoiDung",
                columns: table => new
                {
                    MaNguoiDung = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    TenDangNhap = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    MatKhau = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    Email = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    SoDienThoai = table.Column<string>(type: "character varying(15)", maxLength: 15, nullable: true),
                    DiaChi = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    TrangThai = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    NgayTao = table.Column<DateTime>(type: "timestamp without time zone", nullable: true, defaultValueSql: "CURRENT_TIMESTAMP"),
                    TenNguoiDung = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    MaQuyen = table.Column<int>(type: "integer", nullable: false, defaultValue: 3),
                    AnhDaiDien = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK__NguoiDun__C539D76221D1D8FC", x => x.MaNguoiDung);
                    table.ForeignKey(
                        name: "FK_NguoiDung_PhanQuyen",
                        column: x => x.MaQuyen,
                        principalTable: "PhanQuyen",
                        principalColumn: "MaQuyen");
                });

            migrationBuilder.CreateTable(
                name: "SanPham",
                columns: table => new
                {
                    MaSanPham = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    MaDanhMuc = table.Column<int>(type: "integer", nullable: true),
                    TenSanPham = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    MoTa = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    TrangThaiSanPham = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true, defaultValue: "Còn hàng"),
                    BaoHanh = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    ThongSoKyThuat = table.Column<string>(type: "text", nullable: true),
                    GiaGoc = table.Column<decimal>(type: "numeric(12,2)", nullable: true),
                    MaThuongHieu = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK__SanPham__FAC7442D8E7CBCF5", x => x.MaSanPham);
                    table.ForeignKey(
                        name: "FK_SanPham_DanhMuc",
                        column: x => x.MaDanhMuc,
                        principalTable: "DanhMucSanPham",
                        principalColumn: "MaDanhMuc");
                    table.ForeignKey(
                        name: "FK_SanPham_ThuongHieu",
                        column: x => x.MaThuongHieu,
                        principalTable: "ThuongHieu",
                        principalColumn: "MaThuongHieu");
                });

            migrationBuilder.CreateTable(
                name: "CuocHoiThoaiAI",
                columns: table => new
                {
                    MaCuocHoiThoai = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    MaNguoiDung = table.Column<int>(type: "integer", nullable: false),
                    TieuDe = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    NgayTao = table.Column<DateTime>(type: "timestamp without time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    NgayCapNhat = table.Column<DateTime>(type: "timestamp without time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK__CuocHoiT__3707D875B03839C0", x => x.MaCuocHoiThoai);
                    table.ForeignKey(
                        name: "FK_CuocHoiThoaiAI_NguoiDung",
                        column: x => x.MaNguoiDung,
                        principalTable: "NguoiDung",
                        principalColumn: "MaNguoiDung");
                });

            migrationBuilder.CreateTable(
                name: "DonHang",
                columns: table => new
                {
                    MaDonHang = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    MaNguoiDung = table.Column<int>(type: "integer", nullable: true),
                    NgayDat = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    TongTien = table.Column<decimal>(type: "numeric(12,2)", nullable: true),
                    TrangThaiDonHang = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true, defaultValue: "Chờ xử lý"),
                    HoTenNhanHang = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    SoDienThoaiNhanHang = table.Column<string>(type: "character varying(15)", maxLength: 15, nullable: true),
                    DiaChiNhanHang = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    GhiChu = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    PhuongThucVanChuyen = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    MaKhuyenMai = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK__DonHang__129584AD88A83EF4", x => x.MaDonHang);
                    table.ForeignKey(
                        name: "FK_DonHang_KhuyenMai",
                        column: x => x.MaKhuyenMai,
                        principalTable: "KhuyenMai",
                        principalColumn: "MaKhuyenMai");
                    table.ForeignKey(
                        name: "FK_DonHang_NguoiDung",
                        column: x => x.MaNguoiDung,
                        principalTable: "NguoiDung",
                        principalColumn: "MaNguoiDung");
                });

            migrationBuilder.CreateTable(
                name: "BienTheSanPham",
                columns: table => new
                {
                    MaBienThe = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    MaSanPham = table.Column<int>(type: "integer", nullable: false),
                    MauSac = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    PhienBan = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Gia = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    SoLuong = table.Column<int>(type: "integer", nullable: false),
                    HinhAnh = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    TrangThai = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false, defaultValue: "Còn hàng")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK__BienTheS__3987CEF598B5B7D6", x => x.MaBienThe);
                    table.ForeignKey(
                        name: "FK_BienTheSanPham_SanPham",
                        column: x => x.MaSanPham,
                        principalTable: "SanPham",
                        principalColumn: "MaSanPham");
                });

            migrationBuilder.CreateTable(
                name: "SanPhamHinhAnh",
                columns: table => new
                {
                    MaAnh = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    MaSanPham = table.Column<int>(type: "integer", nullable: false),
                    DuongDanAnh = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    LaAnhChinh = table.Column<bool>(type: "boolean", nullable: false),
                    ThuTu = table.Column<int>(type: "integer", nullable: false, defaultValue: 1)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK__SanPhamH__356240DF6D083CA7", x => x.MaAnh);
                    table.ForeignKey(
                        name: "FK_SanPhamHinhAnh_SanPham",
                        column: x => x.MaSanPham,
                        principalTable: "SanPham",
                        principalColumn: "MaSanPham");
                });

            migrationBuilder.CreateTable(
                name: "YeuThich",
                columns: table => new
                {
                    MaYeuThich = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    MaNguoiDung = table.Column<int>(type: "integer", nullable: false),
                    MaSanPham = table.Column<int>(type: "integer", nullable: false),
                    NgayThem = table.Column<DateTime>(type: "timestamp without time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK__YeuThich__B9007E4CB6025510", x => x.MaYeuThich);
                    table.ForeignKey(
                        name: "FK_YeuThich_NguoiDung",
                        column: x => x.MaNguoiDung,
                        principalTable: "NguoiDung",
                        principalColumn: "MaNguoiDung");
                    table.ForeignKey(
                        name: "FK_YeuThich_SanPham",
                        column: x => x.MaSanPham,
                        principalTable: "SanPham",
                        principalColumn: "MaSanPham");
                });

            migrationBuilder.CreateTable(
                name: "TinNhanAI",
                columns: table => new
                {
                    MaTinNhan = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    MaCuocHoiThoai = table.Column<int>(type: "integer", nullable: false),
                    VaiTro = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    NoiDung = table.Column<string>(type: "text", nullable: false),
                    NgayGui = table.Column<DateTime>(type: "timestamp without time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK__TinNhanA__E5B3062ACADFFD71", x => x.MaTinNhan);
                    table.ForeignKey(
                        name: "FK_TinNhanAI_CuocHoiThoai",
                        column: x => x.MaCuocHoiThoai,
                        principalTable: "CuocHoiThoaiAI",
                        principalColumn: "MaCuocHoiThoai");
                });

            migrationBuilder.CreateTable(
                name: "DanhGiaSanPham",
                columns: table => new
                {
                    MaDanhGia = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    MaNguoiDung = table.Column<int>(type: "integer", nullable: true),
                    MaSanPham = table.Column<int>(type: "integer", nullable: true),
                    SoLuongSao = table.Column<int>(type: "integer", nullable: true),
                    BinhLuan = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    MaDonHang = table.Column<int>(type: "integer", nullable: true),
                    NgayDanhGia = table.Column<DateTime>(type: "timestamp without time zone", nullable: true, defaultValueSql: "CURRENT_TIMESTAMP"),
                    TrangThai = table.Column<bool>(type: "boolean", nullable: true, defaultValue: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK__DanhGiaS__AA9515BFCBF70C5B", x => x.MaDanhGia);
                    table.ForeignKey(
                        name: "FK_DG_NguoiDung",
                        column: x => x.MaNguoiDung,
                        principalTable: "NguoiDung",
                        principalColumn: "MaNguoiDung");
                    table.ForeignKey(
                        name: "FK_DG_SanPham",
                        column: x => x.MaSanPham,
                        principalTable: "SanPham",
                        principalColumn: "MaSanPham");
                    table.ForeignKey(
                        name: "FK_DanhGia_DonHang",
                        column: x => x.MaDonHang,
                        principalTable: "DonHang",
                        principalColumn: "MaDonHang");
                });

            migrationBuilder.CreateTable(
                name: "HoaDon",
                columns: table => new
                {
                    MaHoaDon = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    MaDonHang = table.Column<int>(type: "integer", nullable: false),
                    NgayLapHoaDon = table.Column<DateTime>(type: "timestamp without time zone", nullable: true, defaultValueSql: "CURRENT_TIMESTAMP"),
                    TongTien = table.Column<decimal>(type: "numeric(12,2)", nullable: true),
                    TrangThaiHoaDon = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true, defaultValue: "Đã lập")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK__HoaDon__835ED13BE1E4A10D", x => x.MaHoaDon);
                    table.ForeignKey(
                        name: "FK_HoaDon_DonHang",
                        column: x => x.MaDonHang,
                        principalTable: "DonHang",
                        principalColumn: "MaDonHang");
                });

            migrationBuilder.CreateTable(
                name: "LichSuVanChuyen",
                columns: table => new
                {
                    MaLichSu = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    MaDonHang = table.Column<int>(type: "integer", nullable: false),
                    TrangThai = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    MoTa = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    DiaDiem = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    HinhAnhNguoiNhan = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    ThoiGian = table.Column<DateTime>(type: "timestamp without time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK__LichSuVa__C443222A60038AB5", x => x.MaLichSu);
                    table.ForeignKey(
                        name: "FK_LichSuVanChuyen_DonHang",
                        column: x => x.MaDonHang,
                        principalTable: "DonHang",
                        principalColumn: "MaDonHang");
                });

            migrationBuilder.CreateTable(
                name: "ThanhToan",
                columns: table => new
                {
                    MaThanhToan = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    MaDonHang = table.Column<int>(type: "integer", nullable: false),
                    PhuongThuc = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    SoTien = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    TrangThai = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false, defaultValue: "Chờ thanh toán"),
                    MaGiaoDich = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    NgayTao = table.Column<DateTime>(type: "timestamp without time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    NgayThanhToan = table.Column<DateTime>(type: "timestamp without time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK__ThanhToa__D4B25844E085DA97", x => x.MaThanhToan);
                    table.ForeignKey(
                        name: "FK_ThanhToan_DonHang",
                        column: x => x.MaDonHang,
                        principalTable: "DonHang",
                        principalColumn: "MaDonHang");
                });

            migrationBuilder.CreateTable(
                name: "ChiTietDonHang",
                columns: table => new
                {
                    MaChiTietDonHang = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    MaDonHang = table.Column<int>(type: "integer", nullable: false),
                    SoLuong = table.Column<int>(type: "integer", nullable: false),
                    DonGia = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    MaBienThe = table.Column<int>(type: "integer", nullable: false),
                    ThanhTien = table.Column<decimal>(type: "numeric(18,2)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK__ChiTietD__835ED13B4D53BB16", x => x.MaChiTietDonHang);
                    table.ForeignKey(
                        name: "FK_CTDH_DonHang",
                        column: x => x.MaDonHang,
                        principalTable: "DonHang",
                        principalColumn: "MaDonHang");
                    table.ForeignKey(
                        name: "FK_ChiTietDonHang_BienThe",
                        column: x => x.MaBienThe,
                        principalTable: "BienTheSanPham",
                        principalColumn: "MaBienThe");
                });

            migrationBuilder.CreateTable(
                name: "ChiTietGioHang",
                columns: table => new
                {
                    MaChiTietGioHang = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    MaNguoiDung = table.Column<int>(type: "integer", nullable: false),
                    MaBienThe = table.Column<int>(type: "integer", nullable: false),
                    SoLuong = table.Column<int>(type: "integer", nullable: false, defaultValue: 1),
                    NgayThem = table.Column<DateTime>(type: "timestamp without time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK__ChiTietG__BBF4749877F09C5C", x => x.MaChiTietGioHang);
                    table.ForeignKey(
                        name: "FK_ChiTietGioHang_BienThe",
                        column: x => x.MaBienThe,
                        principalTable: "BienTheSanPham",
                        principalColumn: "MaBienThe");
                    table.ForeignKey(
                        name: "FK_ChiTietGioHang_NguoiDung",
                        column: x => x.MaNguoiDung,
                        principalTable: "NguoiDung",
                        principalColumn: "MaNguoiDung");
                });

            migrationBuilder.CreateIndex(
                name: "IX_BienTheSanPham_MaSanPham",
                table: "BienTheSanPham",
                column: "MaSanPham");

            migrationBuilder.CreateIndex(
                name: "IX_ChiTietDonHang_MaBienThe",
                table: "ChiTietDonHang",
                column: "MaBienThe");

            migrationBuilder.CreateIndex(
                name: "IX_ChiTietDonHang_MaDonHang",
                table: "ChiTietDonHang",
                column: "MaDonHang");

            migrationBuilder.CreateIndex(
                name: "IX_ChiTietGioHang_MaBienThe",
                table: "ChiTietGioHang",
                column: "MaBienThe");

            migrationBuilder.CreateIndex(
                name: "UQ_ChiTietGioHang",
                table: "ChiTietGioHang",
                columns: new[] { "MaNguoiDung", "MaBienThe" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CuocHoiThoaiAI_MaNguoiDung",
                table: "CuocHoiThoaiAI",
                column: "MaNguoiDung");

            migrationBuilder.CreateIndex(
                name: "IX_DanhGiaSanPham_MaDonHang",
                table: "DanhGiaSanPham",
                column: "MaDonHang");

            migrationBuilder.CreateIndex(
                name: "IX_DanhGiaSanPham_MaSanPham",
                table: "DanhGiaSanPham",
                column: "MaSanPham");

            migrationBuilder.CreateIndex(
                name: "UQ_DanhGia_User_Product",
                table: "DanhGiaSanPham",
                columns: new[] { "MaNguoiDung", "MaSanPham" },
                unique: true,
                filter: "\"MaNguoiDung\" IS NOT NULL AND \"MaSanPham\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_DonHang_MaKhuyenMai",
                table: "DonHang",
                column: "MaKhuyenMai");

            migrationBuilder.CreateIndex(
                name: "IX_DonHang_MaNguoiDung",
                table: "DonHang",
                column: "MaNguoiDung");

            migrationBuilder.CreateIndex(
                name: "UQ_HoaDon_MaDonHang",
                table: "HoaDon",
                column: "MaDonHang",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UQ__KhuyenMa__152C7C5CF41E5F9B",
                table: "KhuyenMai",
                column: "MaCode",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LichSuVanChuyen_MaDonHang",
                table: "LichSuVanChuyen",
                column: "MaDonHang");

            migrationBuilder.CreateIndex(
                name: "IX_NguoiDung_Email",
                table: "NguoiDung",
                column: "Email");

            migrationBuilder.CreateIndex(
                name: "IX_NguoiDung_MaQuyen",
                table: "NguoiDung",
                column: "MaQuyen");

            migrationBuilder.CreateIndex(
                name: "IX_NguoiDung_TenNguoiDung",
                table: "NguoiDung",
                column: "TenNguoiDung");

            migrationBuilder.CreateIndex(
                name: "UQ_NguoiDung_Email",
                table: "NguoiDung",
                column: "Email",
                unique: true,
                filter: "\"Email\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "UQ_NguoiDung_TenDangNhap",
                table: "NguoiDung",
                column: "TenDangNhap",
                unique: true,
                filter: "\"TenDangNhap\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "UQ__PhanQuye__5637EE792105E491",
                table: "PhanQuyen",
                column: "TenQuyen",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SanPham_MaDanhMuc",
                table: "SanPham",
                column: "MaDanhMuc");

            migrationBuilder.CreateIndex(
                name: "IX_SanPham_MaThuongHieu",
                table: "SanPham",
                column: "MaThuongHieu");

            migrationBuilder.CreateIndex(
                name: "IX_SanPhamHinhAnh_MaSanPham",
                table: "SanPhamHinhAnh",
                column: "MaSanPham");

            migrationBuilder.CreateIndex(
                name: "IX_ThanhToan_MaDonHang",
                table: "ThanhToan",
                column: "MaDonHang");

            migrationBuilder.CreateIndex(
                name: "UQ__ThuongHi__98D6A834D059DBAD",
                table: "ThuongHieu",
                column: "TenThuongHieu",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TinNhanAI_MaCuocHoiThoai",
                table: "TinNhanAI",
                column: "MaCuocHoiThoai");

            migrationBuilder.CreateIndex(
                name: "UQ__TuCam__C69AFF423679873D",
                table: "TuCam",
                column: "NoiDung",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_YeuThich_MaSanPham",
                table: "YeuThich",
                column: "MaSanPham");

            migrationBuilder.CreateIndex(
                name: "UQ_YeuThich",
                table: "YeuThich",
                columns: new[] { "MaNguoiDung", "MaSanPham" },
                unique: true);
        }
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BaoCao");

            migrationBuilder.DropTable(
                name: "ChiTietDonHang");

            migrationBuilder.DropTable(
                name: "ChiTietGioHang");

            migrationBuilder.DropTable(
                name: "DanhGiaSanPham");

            migrationBuilder.DropTable(
                name: "HoaDon");

            migrationBuilder.DropTable(
                name: "LichSuVanChuyen");

            migrationBuilder.DropTable(
                name: "SanPhamHinhAnh");

            migrationBuilder.DropTable(
                name: "ThanhToan");

            migrationBuilder.DropTable(
                name: "TinNhanAI");

            migrationBuilder.DropTable(
                name: "TuCam");

            migrationBuilder.DropTable(
                name: "YeuThich");

            migrationBuilder.DropTable(
                name: "BienTheSanPham");

            migrationBuilder.DropTable(
                name: "DonHang");

            migrationBuilder.DropTable(
                name: "CuocHoiThoaiAI");

            migrationBuilder.DropTable(
                name: "SanPham");

            migrationBuilder.DropTable(
                name: "KhuyenMai");

            migrationBuilder.DropTable(
                name: "NguoiDung");

            migrationBuilder.DropTable(
                name: "DanhMucSanPham");

            migrationBuilder.DropTable(
                name: "ThuongHieu");

            migrationBuilder.DropTable(
                name: "PhanQuyen");
        }
    }
}
