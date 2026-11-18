using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Xml;

namespace EMua.Services;

public enum ExcelCellType
{
    Text,
    TextCenter,
    Number,
    Currency,
    DateTime,
    DateOnly
}

public class ExcelColumn
{
    public string Header { get; set; } = string.Empty;
    public double Width { get; set; } = 15;
    public ExcelCellType Type { get; set; } = ExcelCellType.Text;
}

public class ExcelExportService
{
    /// <summary>
    /// Xuất dữ liệu ra mảng byte dạng file .xlsx (OpenXML SpreadsheetML) chuẩn quốc tế.
    /// Không phụ thuộc thư viện bên ngoài, chạy được trên mọi môi trường và hệ điều hành.
    /// </summary>
    public static byte[] CreateWorkbook(
        string title,
        string subtitle,
        List<ExcelColumn> columns,
        List<object?[]> rows,
        object?[]? summaryRow = null)
    {
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, true))
        {
            // 1. [Content_Types].xml
            WriteZipEntry(zip, "[Content_Types].xml", GetContentTypesXml());

            // 2. _rels/.rels
            WriteZipEntry(zip, "_rels/.rels", GetRootRelsXml());

            // 3. xl/workbook.xml
            WriteZipEntry(zip, "xl/workbook.xml", GetWorkbookXml());

            // 4. xl/_rels/workbook.xml.rels
            WriteZipEntry(zip, "xl/_rels/workbook.xml.rels", GetWorkbookRelsXml());

            // 5. xl/styles.xml
            WriteZipEntry(zip, "xl/styles.xml", GetStylesXml());

            // 6. xl/worksheets/sheet1.xml
            WriteZipEntry(zip, "xl/worksheets/sheet1.xml", GetWorksheetXml(title, subtitle, columns, rows, summaryRow));
        }

        return ms.ToArray();
    }

    private static void WriteZipEntry(ZipArchive zip, string entryName, string content)
    {
        var entry = zip.CreateEntry(entryName, CompressionLevel.Optimal);
        using var stream = entry.Open();
        using var writer = new StreamWriter(stream, Encoding.UTF8);
        writer.Write(content);
    }

    private static string GetContentTypesXml()
    {
        return "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>\r\n" +
               "<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\">\r\n" +
               "  <Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/>\r\n" +
               "  <Default Extension=\"xml\" ContentType=\"application/xml\"/>\r\n" +
               "  <Override PartName=\"/xl/workbook.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml\"/>\r\n" +
               "  <Override PartName=\"/xl/worksheets/sheet1.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml\"/>\r\n" +
               "  <Override PartName=\"/xl/styles.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml\"/>\r\n" +
               "</Types>";
    }

    private static string GetRootRelsXml()
    {
        return "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>\r\n" +
               "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">\r\n" +
               "  <Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"xl/workbook.xml\"/>\r\n" +
               "</Relationships>";
    }

    private static string GetWorkbookXml()
    {
        return "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>\r\n" +
               "<workbook xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\">\r\n" +
               "  <bookViews><workbookView xWindow=\"0\" yWindow=\"0\" windowWidth=\"20480\" windowHeight=\"10240\"/></bookViews>\r\n" +
               "  <sheets>\r\n" +
               "    <sheet name=\"Báo cáo EMUA\" sheetId=\"1\" r:id=\"rId1\"/>\r\n" +
               "  </sheets>\r\n" +
               "</workbook>";
    }

    private static string GetWorkbookRelsXml()
    {
        return "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>\r\n" +
               "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">\r\n" +
               "  <Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet\" Target=\"worksheets/sheet1.xml\"/>\r\n" +
               "  <Relationship Id=\"rId2\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles\" Target=\"styles.xml\"/>\r\n" +
               "</Relationships>";
    }

    private static string GetStylesXml()
    {
        var sb = new StringBuilder();
        sb.Append("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>\r\n");
        sb.Append("<styleSheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\">\r\n");

        // Custom number formats: 164 = VND (#,##0 "₫"), 165 = dd/mm/yyyy hh:mm, 166 = dd/mm/yyyy
        sb.Append("  <numFmts count=\"3\">\r\n");
        sb.Append("    <numFmt numFmtId=\"164\" formatCode=\"#,##0\\ &quot;₫&quot;\"/>\r\n");
        sb.Append("    <numFmt numFmtId=\"165\" formatCode=\"dd/mm/yyyy\\ hh:mm\"/>\r\n");
        sb.Append("    <numFmt numFmtId=\"166\" formatCode=\"dd/mm/yyyy\"/>\r\n");
        sb.Append("  </numFmts>\r\n");

        // Fonts: 
        // 0: default 10pt Segoe UI
        // 1: bold 10pt
        // 2: title bold 15pt #1E3A8A
        // 3: header bold 10.5pt White
        // 4: subtitle 10pt Italic #64748B
        sb.Append("  <fonts count=\"5\">\r\n");
        sb.Append("    <font><sz val=\"10\"/><color rgb=\"FF172033\"/><name val=\"Segoe UI\"/><family val=\"2\"/></font>\r\n");
        sb.Append("    <font><b/><sz val=\"10\"/><color rgb=\"FF172033\"/><name val=\"Segoe UI\"/><family val=\"2\"/></font>\r\n");
        sb.Append("    <font><b/><sz val=\"15\"/><color rgb=\"FF1E3A8A\"/><name val=\"Segoe UI\"/><family val=\"2\"/></font>\r\n");
        sb.Append("    <font><b/><sz val=\"10.5\"/><color rgb=\"FFFFFFFF\"/><name val=\"Segoe UI\"/><family val=\"2\"/></font>\r\n");
        sb.Append("    <font><i/><sz val=\"10\"/><color rgb=\"FF64748B\"/><name val=\"Segoe UI\"/><family val=\"2\"/></font>\r\n");
        sb.Append("  </fonts>\r\n");

        // Fills:
        // 0: none, 1: gray125, 2: EMUA Navy #1E3A8A, 3: Soft Gray #F8FAFC (summary), 4: Zebra Light #F1F5F9
        sb.Append("  <fills count=\"5\">\r\n");
        sb.Append("    <fill><patternFill patternType=\"none\"/></fill>\r\n");
        sb.Append("    <fill><patternFill patternType=\"gray125\"/></fill>\r\n");
        sb.Append("    <fill><patternFill patternType=\"solid\"><fgColor rgb=\"FF1E3A8A\"/><bgColor indexed=\"64\"/></patternFill></fill>\r\n");
        sb.Append("    <fill><patternFill patternType=\"solid\"><fgColor rgb=\"FFF8FAFC\"/><bgColor indexed=\"64\"/></patternFill></fill>\r\n");
        sb.Append("    <fill><patternFill patternType=\"solid\"><fgColor rgb=\"FFF1F5F9\"/><bgColor indexed=\"64\"/></patternFill></fill>\r\n");
        sb.Append("  </fills>\r\n");

        // Borders:
        // 0: none
        // 1: thin border #CBD5E1 around cell
        // 2: double bottom border for summary row
        sb.Append("  <borders count=\"3\">\r\n");
        sb.Append("    <border><left/><right/><top/><bottom/><diagonal/></border>\r\n");
        sb.Append("    <border>\r\n");
        sb.Append("      <left style=\"thin\"><color rgb=\"FFCBD5E1\"/></left>\r\n");
        sb.Append("      <right style=\"thin\"><color rgb=\"FFCBD5E1\"/></right>\r\n");
        sb.Append("      <top style=\"thin\"><color rgb=\"FFCBD5E1\"/></top>\r\n");
        sb.Append("      <bottom style=\"thin\"><color rgb=\"FFCBD5E1\"/></bottom>\r\n");
        sb.Append("    </border>\r\n");
        sb.Append("    <border>\r\n");
        sb.Append("      <left style=\"thin\"><color rgb=\"FFCBD5E1\"/></left>\r\n");
        sb.Append("      <right style=\"thin\"><color rgb=\"FFCBD5E1\"/></right>\r\n");
        sb.Append("      <top style=\"thin\"><color rgb=\"FFCBD5E1\"/></top>\r\n");
        sb.Append("      <bottom style=\"double\"><color rgb=\"FF1E3A8A\"/></bottom>\r\n");
        sb.Append("    </border>\r\n");
        sb.Append("  </borders>\r\n");

        sb.Append("  <cellStyleXfs count=\"1\">\r\n");
        sb.Append("    <xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\"/>\r\n");
        sb.Append("  </cellStyleXfs>\r\n");

        // Cell Xfs (Styles):
        // 0: normal text (left)
        // 1: title (15pt bold navy)
        // 2: subtitle (italic gray)
        // 3: header cell (navy bg, white bold, center, border thin)
        // 4: data text left (border thin)
        // 5: data text center (border thin)
        // 6: data currency (VND, right, border thin)
        // 7: data number (integer, right, border thin)
        // 8: data date (dd/mm/yyyy hh:mm, center, border thin)
        // 9: summary label (bold, fill soft gray, border double-bottom, right)
        // 10: summary currency (bold, fill soft gray, border double-bottom, VND, right)
        // 11: summary number (bold, fill soft gray, border double-bottom, right)
        // 12: data date only (dd/mm/yyyy, center, border thin)
        sb.Append("  <cellXfs count=\"13\">\r\n");
        sb.Append("    <xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\" xfId=\"0\" applyAlignment=\"1\"><alignment vertical=\"center\"/></xf>\r\n");
        sb.Append("    <xf numFmtId=\"0\" fontId=\"2\" fillId=\"0\" borderId=\"0\" xfId=\"0\" applyFont=\"1\" applyAlignment=\"1\"><alignment vertical=\"center\"/></xf>\r\n");
        sb.Append("    <xf numFmtId=\"0\" fontId=\"4\" fillId=\"0\" borderId=\"0\" xfId=\"0\" applyFont=\"1\" applyAlignment=\"1\"><alignment vertical=\"center\"/></xf>\r\n");
        sb.Append("    <xf numFmtId=\"0\" fontId=\"3\" fillId=\"2\" borderId=\"1\" xfId=\"0\" applyFont=\"1\" applyFill=\"1\" applyBorder=\"1\" applyAlignment=\"1\"><alignment horizontal=\"center\" vertical=\"center\" wrapText=\"1\"/></xf>\r\n");
        sb.Append("    <xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"1\" xfId=\"0\" applyBorder=\"1\" applyAlignment=\"1\"><alignment horizontal=\"left\" vertical=\"center\"/></xf>\r\n");
        sb.Append("    <xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"1\" xfId=\"0\" applyBorder=\"1\" applyAlignment=\"1\"><alignment horizontal=\"center\" vertical=\"center\"/></xf>\r\n");
        sb.Append("    <xf numFmtId=\"164\" fontId=\"0\" fillId=\"0\" borderId=\"1\" xfId=\"0\" applyNumberFormat=\"1\" applyBorder=\"1\" applyAlignment=\"1\"><alignment horizontal=\"right\" vertical=\"center\"/></xf>\r\n");
        sb.Append("    <xf numFmtId=\"3\" fontId=\"0\" fillId=\"0\" borderId=\"1\" xfId=\"0\" applyNumberFormat=\"1\" applyBorder=\"1\" applyAlignment=\"1\"><alignment horizontal=\"right\" vertical=\"center\"/></xf>\r\n");
        sb.Append("    <xf numFmtId=\"165\" fontId=\"0\" fillId=\"0\" borderId=\"1\" xfId=\"0\" applyNumberFormat=\"1\" applyBorder=\"1\" applyAlignment=\"1\"><alignment horizontal=\"center\" vertical=\"center\"/></xf>\r\n");
        sb.Append("    <xf numFmtId=\"0\" fontId=\"1\" fillId=\"3\" borderId=\"2\" xfId=\"0\" applyFont=\"1\" applyFill=\"1\" applyBorder=\"1\" applyAlignment=\"1\"><alignment horizontal=\"right\" vertical=\"center\"/></xf>\r\n");
        sb.Append("    <xf numFmtId=\"164\" fontId=\"1\" fillId=\"3\" borderId=\"2\" xfId=\"0\" applyNumberFormat=\"1\" applyFont=\"1\" applyFill=\"1\" applyBorder=\"1\" applyAlignment=\"1\"><alignment horizontal=\"right\" vertical=\"center\"/></xf>\r\n");
        sb.Append("    <xf numFmtId=\"3\" fontId=\"1\" fillId=\"3\" borderId=\"2\" xfId=\"0\" applyNumberFormat=\"1\" applyFont=\"1\" applyFill=\"1\" applyBorder=\"1\" applyAlignment=\"1\"><alignment horizontal=\"right\" vertical=\"center\"/></xf>\r\n");
        sb.Append("    <xf numFmtId=\"166\" fontId=\"0\" fillId=\"0\" borderId=\"1\" xfId=\"0\" applyNumberFormat=\"1\" applyBorder=\"1\" applyAlignment=\"1\"><alignment horizontal=\"center\" vertical=\"center\"/></xf>\r\n");
        sb.Append("  </cellXfs>\r\n");

        sb.Append("  <cellStyles count=\"1\"><cellStyle name=\"Normal\" xfId=\"0\" builtinId=\"0\"/></cellStyles>\r\n");
        sb.Append("</styleSheet>");
        return sb.ToString();
    }

    private static string GetWorksheetXml(
        string title,
        string subtitle,
        List<ExcelColumn> columns,
        List<object?[]> rows,
        object?[]? summaryRow)
    {
        var sb = new StringBuilder();
        sb.Append("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>\r\n");
        sb.Append("<worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\">\r\n");

        // Sheet views: Gridlines visible
        sb.Append("  <sheetViews>\r\n");
        sb.Append("    <sheetView tabSelected=\"1\" workbookViewId=\"0\" showGridLines=\"1\">\r\n");
        sb.Append("      <pane ySplit=\"4\" topLeftCell=\"A5\" activePane=\"bottomLeft\" state=\"frozen\"/>\r\n");
        sb.Append("    </sheetView>\r\n");
        sb.Append("  </sheetViews>\r\n");
        sb.Append("  <sheetFormatPr defaultRowHeight=\"20\"/>\r\n");

        // Column widths
        sb.Append("  <cols>\r\n");
        for (int i = 0; i < columns.Count; i++)
        {
            var colWidth = Math.Max(columns[i].Width, 12);
            sb.Append($"    <col min=\"{i + 1}\" max=\"{i + 1}\" width=\"{colWidth:0.0}\" customWidth=\"1\"/>\r\n");
        }
        sb.Append("  </cols>\r\n");

        sb.Append("  <sheetData>\r\n");

        int rowIndex = 1;

        // Row 1: Title
        sb.Append($"    <row r=\"{rowIndex}\" ht=\"28\" customHeight=\"1\">\r\n");
        sb.Append($"      <c r=\"A{rowIndex}\" s=\"1\" t=\"inlineStr\"><is><t>{EscapeXml(title)}</t></is></c>\r\n");
        sb.Append("    </row>\r\n");
        rowIndex++;

        // Row 2: Subtitle / Export timestamp
        sb.Append($"    <row r=\"{rowIndex}\" ht=\"20\" customHeight=\"1\">\r\n");
        sb.Append($"      <c r=\"A{rowIndex}\" s=\"2\" t=\"inlineStr\"><is><t>{EscapeXml(subtitle)}</t></is></c>\r\n");
        sb.Append("    </row>\r\n");
        rowIndex++;

        // Row 3: Blank separator
        sb.Append($"    <row r=\"{rowIndex}\" ht=\"8\" customHeight=\"1\"/>\r\n");
        rowIndex++;

        // Row 4: Table Header
        int headerRowIndex = rowIndex;
        sb.Append($"    <row r=\"{headerRowIndex}\" ht=\"26\" customHeight=\"1\">\r\n");
        for (int c = 0; c < columns.Count; c++)
        {
            var cellRef = GetCellRef(c + 1, headerRowIndex);
            sb.Append($"      <c r=\"{cellRef}\" s=\"3\" t=\"inlineStr\"><is><t>{EscapeXml(columns[c].Header)}</t></is></c>\r\n");
        }
        sb.Append("    </row>\r\n");
        rowIndex++;

        // Data Rows
        foreach (var rowData in rows)
        {
            sb.Append($"    <row r=\"{rowIndex}\" ht=\"22\" customHeight=\"1\">\r\n");
            for (int c = 0; c < columns.Count; c++)
            {
                var cellRef = GetCellRef(c + 1, rowIndex);
                var val = c < rowData.Length ? rowData[c] : null;
                var colType = columns[c].Type;

                AppendDataCell(sb, cellRef, val, colType, isSummary: false);
            }
            sb.Append("    </row>\r\n");
            rowIndex++;
        }

        // Summary Row (nếu có)
        if (summaryRow != null)
        {
            sb.Append($"    <row r=\"{rowIndex}\" ht=\"24\" customHeight=\"1\">\r\n");
            for (int c = 0; c < columns.Count; c++)
            {
                var cellRef = GetCellRef(c + 1, rowIndex);
                var val = c < summaryRow.Length ? summaryRow[c] : null;
                var colType = columns[c].Type;

                AppendDataCell(sb, cellRef, val, colType, isSummary: true);
            }
            sb.Append("    </row>\r\n");
            rowIndex++;
        }

        sb.Append("  </sheetData>\r\n");

        // Merge cells for Title row A1:E1
        int lastColIndex = Math.Max(1, columns.Count);
        string lastColLetter = GetColumnLetter(lastColIndex);
        sb.Append("  <mergeCells count=\"2\">\r\n");
        sb.Append($"    <mergeCell ref=\"A1:{lastColLetter}1\"/>\r\n");
        sb.Append($"    <mergeCell ref=\"A2:{lastColLetter}2\"/>\r\n");
        sb.Append("  </mergeCells>\r\n");

        sb.Append("</worksheet>");
        return sb.ToString();
    }

    private static void AppendDataCell(StringBuilder sb, string cellRef, object? val, ExcelCellType colType, bool isSummary)
    {
        if (val == null)
        {
            int styleId = isSummary ? 9 : 4;
            sb.Append($"      <c r=\"{cellRef}\" s=\"{styleId}\"/>\r\n");
            return;
        }

        switch (colType)
        {
            case ExcelCellType.Currency:
                {
                    decimal d = Convert.ToDecimal(val);
                    int styleId = isSummary ? 10 : 6;
                    sb.Append($"      <c r=\"{cellRef}\" s=\"{styleId}\"><v>{d.ToString(System.Globalization.CultureInfo.InvariantCulture)}</v></c>\r\n");
                    break;
                }
            case ExcelCellType.Number:
                {
                    double num = Convert.ToDouble(val);
                    int styleId = isSummary ? 11 : 7;
                    sb.Append($"      <c r=\"{cellRef}\" s=\"{styleId}\"><v>{num.ToString(System.Globalization.CultureInfo.InvariantCulture)}</v></c>\r\n");
                    break;
                }
            case ExcelCellType.DateTime:
                {
                    if (val is DateTime dt)
                    {
                        double oaDate = dt.ToOADate();
                        sb.Append($"      <c r=\"{cellRef}\" s=\"8\"><v>{oaDate.ToString(System.Globalization.CultureInfo.InvariantCulture)}</v></c>\r\n");
                    }
                    else
                    {
                        sb.Append($"      <c r=\"{cellRef}\" s=\"5\" t=\"inlineStr\"><is><t>{EscapeXml(val.ToString())}</t></is></c>\r\n");
                    }
                    break;
                }
            case ExcelCellType.DateOnly:
                {
                    if (val is DateTime dt)
                    {
                        double oaDate = dt.ToOADate();
                        sb.Append($"      <c r=\"{cellRef}\" s=\"12\"><v>{oaDate.ToString(System.Globalization.CultureInfo.InvariantCulture)}</v></c>\r\n");
                    }
                    else
                    {
                        sb.Append($"      <c r=\"{cellRef}\" s=\"5\" t=\"inlineStr\"><is><t>{EscapeXml(val.ToString())}</t></is></c>\r\n");
                    }
                    break;
                }
            case ExcelCellType.TextCenter:
                {
                    int styleId = isSummary ? 9 : 5;
                    sb.Append($"      <c r=\"{cellRef}\" s=\"{styleId}\" t=\"inlineStr\"><is><t>{EscapeXml(val.ToString())}</t></is></c>\r\n");
                    break;
                }
            default: // Text
                {
                    int styleId = isSummary ? 9 : 4;
                    sb.Append($"      <c r=\"{cellRef}\" s=\"{styleId}\" t=\"inlineStr\"><is><t>{EscapeXml(val.ToString())}</t></is></c>\r\n");
                    break;
                }
        }
    }

    private static string GetCellRef(int colIndex, int rowIndex)
    {
        return $"{GetColumnLetter(colIndex)}{rowIndex}";
    }

    private static string GetColumnLetter(int colIndex)
    {
        string letter = "";
        while (colIndex > 0)
        {
            int mod = (colIndex - 1) % 26;
            letter = (char)(65 + mod) + letter;
            colIndex = (colIndex - mod) / 26;
        }
        return letter;
    }

    private static string EscapeXml(string? text)
    {
        if (string.IsNullOrEmpty(text)) return "";
        return text
            .Replace("&", "&amp;")
            .Replace("<", "&lt;")
            .Replace(">", "&gt;")
            .Replace("\"", "&quot;")
            .Replace("'", "&apos;");
    }
}
