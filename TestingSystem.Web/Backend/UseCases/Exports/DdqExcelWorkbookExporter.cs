using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Xml;

namespace TestingSystem.Web.Components.Frontend.UseCases;

internal sealed record DdqWorkbookSheet(string Name, string Title,
    IReadOnlyList<(string Label, string Value)> Metadata,
    IReadOnlyList<string> Headers, IReadOnlyList<IReadOnlyList<object?>> Rows);

internal static class DdqExcelWorkbookExporter
{
    public static byte[] Build(IReadOnlyList<DdqWorkbookSheet> sheets)
    {
        using var output = new MemoryStream();
        using (var archive = new ZipArchive(output, ZipArchiveMode.Create, true))
        {
            Write(archive, "[Content_Types].xml", ContentTypes(sheets.Count));
            Write(archive, "_rels/.rels", RootRelationships);
            Write(archive, "xl/workbook.xml", Workbook(sheets));
            Write(archive, "xl/_rels/workbook.xml.rels", WorkbookRelationships(sheets.Count));
            Write(archive, "xl/styles.xml", Styles);
            for (var index = 0; index < sheets.Count; index++)
                Write(archive, $"xl/worksheets/sheet{index + 1}.xml", Worksheet(sheets[index]));
        }
        return output.ToArray();
    }

    private static void Write(ZipArchive archive, string path, string content)
    {
        var entry = archive.CreateEntry(path, CompressionLevel.Fastest);
        using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false));
        writer.Write(content);
    }

    private static string ContentTypes(int count)
    {
        var sheets = string.Concat(Enumerable.Range(1, count).Select(index =>
            $"<Override PartName=\"/xl/worksheets/sheet{index}.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml\"/>"));
        return $"<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\"><Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/><Default Extension=\"xml\" ContentType=\"application/xml\"/><Override PartName=\"/xl/workbook.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml\"/><Override PartName=\"/xl/styles.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml\"/>{sheets}</Types>";
    }

    private static string Workbook(IReadOnlyList<DdqWorkbookSheet> sheets)
    {
        var entries = string.Concat(sheets.Select((sheet, index) =>
            $"<sheet name=\"{EscapeAttribute(SheetName(sheet.Name))}\" sheetId=\"{index + 1}\" r:id=\"rId{index + 1}\"/>"));
        return $"<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><workbook xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\"><sheets>{entries}</sheets></workbook>";
    }

    private static string WorkbookRelationships(int count)
    {
        var sheets = string.Concat(Enumerable.Range(1, count).Select(index =>
            $"<Relationship Id=\"rId{index}\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet\" Target=\"worksheets/sheet{index}.xml\"/>"));
        return $"<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">{sheets}<Relationship Id=\"rId{count + 1}\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles\" Target=\"styles.xml\"/></Relationships>";
    }

    private static string Worksheet(DdqWorkbookSheet sheet)
    {
        var csvColumns = sheet.Headers.Count;
        var lastColumn = ColumnName(Math.Max(1, csvColumns));
        var lastRow = sheet.Rows.Count + 6;
        var xml = new StringBuilder();
        xml.Append("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\">");
        xml.Append("<sheetViews><sheetView workbookViewId=\"0\"><pane xSplit=\"3\" ySplit=\"6\" topLeftCell=\"D7\" activePane=\"bottomRight\" state=\"frozen\"/></sheetView></sheetViews>");
        xml.Append("<cols>");
        for (var column = 1; column <= csvColumns; column++)
        {
            var width = column switch { 1 => 8, 2 or 3 => 19, 4 => 21, 5 or 6 or 7 or 8 => 10, _ => 22 };
            xml.Append($"<col min=\"{column}\" max=\"{column}\" width=\"{width}\" customWidth=\"1\"/>");
        }
        xml.Append("</cols><sheetData>");
        xml.Append(Row(1, [sheet.Title], 1));
        xml.Append(Row(2, sheet.Metadata.Select(item => (object?)item.Label).ToArray(), 3));
        xml.Append(Row(3, sheet.Metadata.Select(item => (object?)item.Value).ToArray(), 4));
        xml.Append(Row(4, [$"Generated {DateTime.Now:dd MMM yyyy HH:mm}", $"{sheet.Rows.Count:N0} record(s)"], 0));
        xml.Append(Row(6, sheet.Headers.Cast<object?>().ToArray(), 2));
        for (var index = 0; index < sheet.Rows.Count; index++) xml.Append(Row(index + 7, sheet.Rows[index], 0));
        xml.Append($"</sheetData><autoFilter ref=\"A6:{lastColumn}{Math.Max(6, lastRow)}\"/><mergeCells count=\"1\"><mergeCell ref=\"A1:{lastColumn}1\"/></mergeCells></worksheet>");
        return xml.ToString();
    }

    private static string Row(int rowNumber, IReadOnlyList<object?> values, int style)
    {
        var cells = new StringBuilder($"<row r=\"{rowNumber}\">");
        for (var index = 0; index < values.Count; index++)
        {
            var reference = $"{ColumnName(index + 1)}{rowNumber}";
            var value = values[index];
            if (value is byte or short or int or long or float or double or decimal)
                cells.Append($"<c r=\"{reference}\" s=\"{style}\"><v>{Convert.ToString(value, CultureInfo.InvariantCulture)}</v></c>");
            else
                cells.Append($"<c r=\"{reference}\" s=\"{style}\" t=\"inlineStr\"><is><t xml:space=\"preserve\">{EscapeText(Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty)}</t></is></c>");
        }
        return cells.Append("</row>").ToString();
    }

    private static string ColumnName(int column)
    {
        var name = string.Empty;
        while (column > 0) { column--; name = (char)('A' + column % 26) + name; column /= 26; }
        return name;
    }

    private static string SheetName(string value)
    {
        var invalid = new[] { ':', '\\', '/', '?', '*', '[', ']' };
        var cleaned = new string(value.Select(character => invalid.Contains(character) ? '-' : character).ToArray());
        return cleaned.Length > 31 ? cleaned[..31] : cleaned;
    }

    private static string EscapeText(string value) => SecurityElementEscape(value);
    private static string EscapeAttribute(string value) => SecurityElementEscape(value);
    private static string SecurityElementEscape(string value)
    {
        var builder = new StringBuilder();
        using var writer = XmlWriter.Create(builder, new XmlWriterSettings { ConformanceLevel = ConformanceLevel.Fragment, OmitXmlDeclaration = true });
        writer.WriteString(value);
        writer.Flush();
        return builder.ToString();
    }

    private const string RootRelationships = "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"xl/workbook.xml\"/></Relationships>";
    private const string Styles = "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><styleSheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\"><fonts count=\"4\"><font><sz val=\"11\"/><name val=\"Aptos\"/></font><font><b/><color rgb=\"FFFFFFFF\"/><sz val=\"15\"/><name val=\"Aptos Display\"/></font><font><b/><color rgb=\"FFFFFFFF\"/><sz val=\"11\"/><name val=\"Aptos\"/></font><font><b/><color rgb=\"FF5D6D60\"/><sz val=\"10\"/><name val=\"Aptos\"/></font></fonts><fills count=\"5\"><fill><patternFill patternType=\"none\"/></fill><fill><patternFill patternType=\"gray125\"/></fill><fill><patternFill patternType=\"solid\"><fgColor rgb=\"FF1F6F43\"/><bgColor indexed=\"64\"/></patternFill></fill><fill><patternFill patternType=\"solid\"><fgColor rgb=\"FF0875C9\"/><bgColor indexed=\"64\"/></patternFill></fill><fill><patternFill patternType=\"solid\"><fgColor rgb=\"FFF4F8F2\"/><bgColor indexed=\"64\"/></patternFill></fill></fills><borders count=\"2\"><border/><border><left style=\"thin\"><color rgb=\"FFD0D7DE\"/></left><right style=\"thin\"><color rgb=\"FFD0D7DE\"/></right><top style=\"thin\"><color rgb=\"FFD0D7DE\"/></top><bottom style=\"thin\"><color rgb=\"FFD0D7DE\"/></bottom></border></borders><cellStyleXfs count=\"1\"><xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\"/></cellStyleXfs><cellXfs count=\"5\"><xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"1\" xfId=\"0\"/><xf numFmtId=\"0\" fontId=\"1\" fillId=\"2\" borderId=\"0\" xfId=\"0\" applyAlignment=\"1\"><alignment vertical=\"center\"/></xf><xf numFmtId=\"0\" fontId=\"2\" fillId=\"3\" borderId=\"1\" xfId=\"0\" applyAlignment=\"1\"><alignment horizontal=\"center\" vertical=\"center\" wrapText=\"1\"/></xf><xf numFmtId=\"0\" fontId=\"3\" fillId=\"4\" borderId=\"1\" xfId=\"0\"/><xf numFmtId=\"0\" fontId=\"0\" fillId=\"4\" borderId=\"1\" xfId=\"0\"/></cellXfs><cellStyles count=\"1\"><cellStyle name=\"Normal\" xfId=\"0\" builtinId=\"0\"/></cellStyles></styleSheet>";
}
