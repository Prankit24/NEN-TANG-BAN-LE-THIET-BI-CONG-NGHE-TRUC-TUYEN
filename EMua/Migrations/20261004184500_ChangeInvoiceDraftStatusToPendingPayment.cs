using EMua.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace EMua.Migrations;

[DbContext(typeof(EMuaDbContext))]
[Migration("20261004184500_ChangeInvoiceDraftStatusToPendingPayment")]
public sealed class ChangeInvoiceDraftStatusToPendingPayment : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            DO $$
            DECLARE
                updated_rows integer;
            BEGIN
                UPDATE "HoaDon"
                SET "TrangThaiHoaDon" = 'Chờ thanh toán'
                WHERE "TrangThaiHoaDon" = 'Đã lập';

                GET DIAGNOSTICS updated_rows = ROW_COUNT;
                IF updated_rows <> 2 THEN
                    RAISE EXCEPTION 'Expected to update 2 invoices, but updated %.', updated_rows;
                END IF;
            END $$;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
    }
}