using System;
using System.Collections.Generic;
using EMua.Models.Database;
using Microsoft.EntityFrameworkCore;

namespace EMua.Data;

public partial class EMuaDbContext : DbContext
{
    public EMuaDbContext()
    {
    }

    public EMuaDbContext(DbContextOptions<EMuaDbContext> options)
        : base(options)
    {
    }

    public virtual DbSet<BaoCao> BaoCaos { get; set; }
    public virtual DbSet<BienTheSanPham> BienTheSanPhams { get; set; }
    public virtual DbSet<ChiTietDonHang> ChiTietDonHangs { get; set; }
    public virtual DbSet<ChiTietGioHang> ChiTietGioHangs { get; set; }
    public virtual DbSet<CuocHoiThoaiAi> CuocHoiThoaiAis { get; set; }
    public virtual DbSet<DanhGiaSanPham> DanhGiaSanPhams { get; set; }
    public virtual DbSet<DanhMucSanPham> DanhMucSanPhams { get; set; }
    public virtual DbSet<DonHang> DonHangs { get; set; }
    public virtual DbSet<HoaDon> HoaDons { get; set; }
    public virtual DbSet<KhuyenMai> KhuyenMais { get; set; }
    public virtual DbSet<LichSuVanChuyen> LichSuVanChuyens { get; set; }
    public virtual DbSet<NguoiDung> NguoiDungs { get; set; }
    public virtual DbSet<PhanQuyen> PhanQuyens { get; set; }
    public virtual DbSet<SanPham> SanPhams { get; set; }
    public virtual DbSet<SanPhamHinhAnh> SanPhamHinhAnhs { get; set; }
    public virtual DbSet<ThanhToan> ThanhToans { get; set; }
    public virtual DbSet<ThuongHieu> ThuongHieus { get; set; }
    public virtual DbSet<TinNhanAi> TinNhanAis { get; set; }
    public virtual DbSet<TuCam> TuCams { get; set; }
    public virtual DbSet<YeuThich> YeuThiches { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<BaoCao>(entity =>
        {
            entity.HasKey(e => e.MaBaoCao).HasName("PK__BaoCao__25A9188CF7650BAA");
        });

        modelBuilder.Entity<BienTheSanPham>(entity =>
        {
            entity.HasKey(e => e.MaBienThe).HasName("PK__BienTheS__3987CEF598B5B7D6");

            entity.Property(e => e.TrangThai).HasDefaultValue("Còn hàng");

            entity.HasOne(d => d.MaSanPhamNavigation).WithMany(p => p.BienTheSanPhams)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_BienTheSanPham_SanPham");
        });

        modelBuilder.Entity<ChiTietDonHang>(entity =>
        {
            entity.HasKey(e => e.MaChiTietDonHang).HasName("PK__ChiTietD__835ED13B4D53BB16");

            entity.HasOne(d => d.MaBienTheNavigation).WithMany(p => p.ChiTietDonHangs)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_ChiTietDonHang_BienThe");

            entity.HasOne(d => d.MaDonHangNavigation).WithMany(p => p.ChiTietDonHangs)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_CTDH_DonHang");
        });

        modelBuilder.Entity<ChiTietGioHang>(entity =>
        {
            entity.HasKey(e => e.MaChiTietGioHang).HasName("PK__ChiTietG__BBF4749877F09C5C");

            entity.Property(e => e.NgayThem).HasDefaultValueSql("CURRENT_TIMESTAMP");
            entity.Property(e => e.SoLuong).HasDefaultValue(1);

            entity.HasOne(d => d.MaBienTheNavigation).WithMany(p => p.ChiTietGioHangs)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_ChiTietGioHang_BienThe");

            entity.HasOne(d => d.MaNguoiDungNavigation).WithMany(p => p.ChiTietGioHangs)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_ChiTietGioHang_NguoiDung");
        });

        modelBuilder.Entity<CuocHoiThoaiAi>(entity =>
        {
            entity.HasKey(e => e.MaCuocHoiThoai).HasName("PK__CuocHoiT__3707D875B03839C0");

            entity.Property(e => e.NgayTao).HasDefaultValueSql("CURRENT_TIMESTAMP");

            entity.HasOne(d => d.MaNguoiDungNavigation).WithMany(p => p.CuocHoiThoaiAis)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_CuocHoiThoaiAI_NguoiDung");
        });

        modelBuilder.Entity<DanhGiaSanPham>(entity =>
        {
            entity.HasKey(e => e.MaDanhGia).HasName("PK__DanhGiaS__AA9515BFCBF70C5B");

            entity.HasIndex(e => new { e.MaNguoiDung, e.MaSanPham }, "UQ_DanhGia_User_Product")
                .IsUnique()
                .HasFilter("\"MaNguoiDung\" IS NOT NULL AND \"MaSanPham\" IS NOT NULL");

            entity.Property(e => e.NgayDanhGia).HasDefaultValueSql("CURRENT_TIMESTAMP");
            entity.Property(e => e.TrangThai).HasDefaultValue(true);

            entity.HasOne(d => d.MaDonHangNavigation).WithMany(p => p.DanhGiaSanPhams)
                .HasConstraintName("FK_DanhGia_DonHang");

            entity.HasOne(d => d.MaNguoiDungNavigation).WithMany(p => p.DanhGiaSanPhams)
                .HasConstraintName("FK_DG_NguoiDung");

            entity.HasOne(d => d.MaSanPhamNavigation).WithMany(p => p.DanhGiaSanPhams)
                .HasConstraintName("FK_DG_SanPham");
        });

        modelBuilder.Entity<DanhMucSanPham>(entity =>
        {
            entity.HasKey(e => e.MaDanhMuc).HasName("PK__DanhMucS__B37508871346CE70");
        });

        modelBuilder.Entity<DonHang>(entity =>
        {
            entity.HasKey(e => e.MaDonHang).HasName("PK__DonHang__129584AD88A83EF4");

            entity.Property(e => e.TrangThaiDonHang).HasDefaultValue("Chờ xử lý");

            entity.HasOne(d => d.MaKhuyenMaiNavigation).WithMany(p => p.DonHangs)
                .HasConstraintName("FK_DonHang_KhuyenMai");

            entity.HasOne(d => d.MaNguoiDungNavigation).WithMany(p => p.DonHangs)
                .HasConstraintName("FK_DonHang_NguoiDung");
        });

        modelBuilder.Entity<HoaDon>(entity =>
        {
            entity.HasKey(e => e.MaHoaDon).HasName("PK__HoaDon__835ED13BE1E4A10D");

            entity.Property(e => e.NgayLapHoaDon).HasDefaultValueSql("CURRENT_TIMESTAMP");
            entity.Property(e => e.TrangThaiHoaDon).HasDefaultValue("Đã lập");

            entity.HasOne(d => d.MaDonHangNavigation).WithOne(p => p.HoaDon)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_HoaDon_DonHang");
        });

        modelBuilder.Entity<KhuyenMai>(entity =>
        {
            entity.HasKey(e => e.MaKhuyenMai).HasName("PK__KhuyenMa__6F56B3BD8771E9E5");

            entity.Property(e => e.TrangThai).HasDefaultValue(true);
        });

        modelBuilder.Entity<LichSuVanChuyen>(entity =>
        {
            entity.HasKey(e => e.MaLichSu).HasName("PK__LichSuVa__C443222A60038AB5");

            entity.Property(e => e.ThoiGian).HasDefaultValueSql("CURRENT_TIMESTAMP");

            entity.HasOne(d => d.MaDonHangNavigation).WithMany(p => p.LichSuVanChuyens)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_LichSuVanChuyen_DonHang");
        });

        modelBuilder.Entity<NguoiDung>(entity =>
        {
            entity.HasKey(e => e.MaNguoiDung).HasName("PK__NguoiDun__C539D76221D1D8FC");

            entity.HasIndex(e => e.Email, "UQ_NguoiDung_Email")
                .IsUnique()
                .HasFilter("\"Email\" IS NOT NULL");

            entity.HasIndex(e => e.TenDangNhap, "UQ_NguoiDung_TenDangNhap")
                .IsUnique()
                .HasFilter("\"TenDangNhap\" IS NOT NULL");

            entity.Property(e => e.MaQuyen).HasDefaultValue(3);
            entity.Property(e => e.NgayTao).HasDefaultValueSql("CURRENT_TIMESTAMP");
            entity.Property(e => e.TrangThai).HasDefaultValue(true);

            entity.HasOne(d => d.MaQuyenNavigation).WithMany(p => p.NguoiDungs)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_NguoiDung_PhanQuyen");
        });

        modelBuilder.Entity<PhanQuyen>(entity =>
        {
            entity.HasKey(e => e.MaQuyen).HasName("PK__PhanQuye__1D4B7ED47D31A58F");
        });

        modelBuilder.Entity<SanPham>(entity =>
        {
            entity.HasKey(e => e.MaSanPham).HasName("PK__SanPham__FAC7442D8E7CBCF5");

            entity.Property(e => e.TrangThaiSanPham).HasDefaultValue("Còn hàng");

            entity.HasOne(d => d.MaDanhMucNavigation).WithMany(p => p.SanPhams)
                .HasConstraintName("FK_SanPham_DanhMuc");

            entity.HasOne(d => d.MaThuongHieuNavigation).WithMany(p => p.SanPhams)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_SanPham_ThuongHieu");
        });

        modelBuilder.Entity<SanPhamHinhAnh>(entity =>
        {
            entity.HasKey(e => e.MaAnh).HasName("PK__SanPhamH__356240DF6D083CA7");

            entity.Property(e => e.ThuTu).HasDefaultValue(1);

            entity.HasOne(d => d.MaSanPhamNavigation).WithMany(p => p.SanPhamHinhAnhs)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_SanPhamHinhAnh_SanPham");
        });

        modelBuilder.Entity<ThanhToan>(entity =>
        {
            entity.HasKey(e => e.MaThanhToan).HasName("PK__ThanhToa__D4B25844E085DA97");

            entity.Property(e => e.NgayTao).HasDefaultValueSql("CURRENT_TIMESTAMP");
            entity.Property(e => e.TrangThai).HasDefaultValue("Chờ thanh toán");

            entity.HasOne(d => d.MaDonHangNavigation).WithMany(p => p.ThanhToans)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_ThanhToan_DonHang");
        });

        modelBuilder.Entity<ThuongHieu>(entity =>
        {
            entity.HasKey(e => e.MaThuongHieu).HasName("PK__ThuongHi__A3733E2C0EF3AC57");

            entity.Property(e => e.TrangThai).HasDefaultValue(true);
        });

        modelBuilder.Entity<TinNhanAi>(entity =>
        {
            entity.HasKey(e => e.MaTinNhan).HasName("PK__TinNhanA__E5B3062ACADFFD71");

            entity.Property(e => e.NgayGui).HasDefaultValueSql("CURRENT_TIMESTAMP");

            entity.HasOne(d => d.MaCuocHoiThoaiNavigation).WithMany(p => p.TinNhanAis)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_TinNhanAI_CuocHoiThoai");
        });

        modelBuilder.Entity<TuCam>(entity =>
        {
            entity.HasKey(e => e.MaTuCam).HasName("PK__TuCam__B89513110632111A");

            entity.Property(e => e.NgayTao).HasDefaultValueSql("CURRENT_TIMESTAMP");
            entity.Property(e => e.TrangThai).HasDefaultValue(true);
        });

        modelBuilder.Entity<YeuThich>(entity =>
        {
            entity.HasKey(e => e.MaYeuThich).HasName("PK__YeuThich__B9007E4CB6025510");

            entity.Property(e => e.NgayThem).HasDefaultValueSql("CURRENT_TIMESTAMP");

            entity.HasOne(d => d.MaNguoiDungNavigation).WithMany(p => p.YeuThiches)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_YeuThich_NguoiDung");

            entity.HasOne(d => d.MaSanPhamNavigation).WithMany(p => p.YeuThiches)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_YeuThich_SanPham");
        });

        OnModelCreatingPartial(modelBuilder);
    }

    partial void OnModelCreatingPartial(ModelBuilder modelBuilder);
}