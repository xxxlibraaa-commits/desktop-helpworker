using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using System.IO;

namespace FloatMate.Services;

internal static class SpreadsheetExportCompatibility
{
    public static void AssignCellReferences(SheetData sheetData)
    {
        uint rowIndex = 1;
        foreach (var row in sheetData.Elements<Row>())
        {
            row.RowIndex = rowIndex;
            var columnIndex = 1;
            foreach (var cell in row.Elements<Cell>()) cell.CellReference = $"{ColumnName(columnIndex++)}{rowIndex}";
            rowIndex++;
        }
    }

    public static void Add(WorkbookPart workbookPart)
    {
        var sharedStrings = workbookPart.AddNewPart<SharedStringTablePart>();
        sharedStrings.SharedStringTable = new SharedStringTable { Count = 0U, UniqueCount = 0U };
        sharedStrings.SharedStringTable.Save();

        const string themeXml = """
<?xml version="1.0" encoding="utf-8"?><a:theme name="FloatMate" xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main"><a:themeElements><a:clrScheme name="FloatMate"><a:dk1><a:sysClr val="windowText" lastClr="000000" /></a:dk1><a:lt1><a:sysClr val="window" lastClr="FFFFFF" /></a:lt1><a:dk2><a:srgbClr val="1C1C1E" /></a:dk2><a:lt2><a:srgbClr val="F2F2F7" /></a:lt2><a:accent1><a:srgbClr val="0066CC" /></a:accent1><a:accent2><a:srgbClr val="1F7A35" /></a:accent2><a:accent3><a:srgbClr val="5A5A60" /></a:accent3><a:accent4><a:srgbClr val="007AFF" /></a:accent4><a:accent5><a:srgbClr val="A35A00" /></a:accent5><a:accent6><a:srgbClr val="63636A" /></a:accent6><a:hlink><a:srgbClr val="0066CC" /></a:hlink><a:folHlink><a:srgbClr val="004C99" /></a:folHlink></a:clrScheme><a:fontScheme name="FloatMate"><a:majorFont><a:latin typeface="Microsoft YaHei UI" /><a:ea typeface="Microsoft YaHei UI" /><a:cs typeface="Microsoft YaHei UI" /></a:majorFont><a:minorFont><a:latin typeface="Microsoft YaHei UI" /><a:ea typeface="Microsoft YaHei UI" /><a:cs typeface="Microsoft YaHei UI" /></a:minorFont></a:fontScheme><a:fmtScheme name="FloatMate"><a:fillStyleLst><a:solidFill><a:schemeClr val="phClr" /></a:solidFill><a:gradFill><a:gsLst><a:gs pos="0"><a:schemeClr val="phClr"><a:tint val="67000" /><a:lumMod val="110000" /></a:schemeClr></a:gs><a:gs pos="100000"><a:schemeClr val="phClr"><a:tint val="81000" /><a:lumMod val="105000" /></a:schemeClr></a:gs></a:gsLst><a:lin ang="5400000" scaled="0" /></a:gradFill><a:gradFill><a:gsLst><a:gs pos="0"><a:schemeClr val="phClr"><a:tint val="94000" /></a:schemeClr></a:gs><a:gs pos="100000"><a:schemeClr val="phClr"><a:shade val="78000" /></a:schemeClr></a:gs></a:gsLst><a:lin ang="5400000" scaled="0" /></a:gradFill></a:fillStyleLst><a:lnStyleLst><a:ln w="12700"><a:solidFill><a:schemeClr val="phClr" /></a:solidFill><a:prstDash val="solid" /></a:ln><a:ln w="19050"><a:solidFill><a:schemeClr val="phClr" /></a:solidFill><a:prstDash val="solid" /></a:ln><a:ln w="25400"><a:solidFill><a:schemeClr val="phClr" /></a:solidFill><a:prstDash val="solid" /></a:ln></a:lnStyleLst><a:effectStyleLst><a:effectStyle><a:effectLst /></a:effectStyle><a:effectStyle><a:effectLst /></a:effectStyle><a:effectStyle><a:effectLst /></a:effectStyle></a:effectStyleLst><a:bgFillStyleLst><a:solidFill><a:schemeClr val="phClr" /></a:solidFill><a:solidFill><a:schemeClr val="phClr"><a:tint val="95000" /></a:schemeClr></a:solidFill><a:gradFill><a:gsLst><a:gs pos="0"><a:schemeClr val="phClr"><a:tint val="93000" /></a:schemeClr></a:gs><a:gs pos="100000"><a:schemeClr val="phClr"><a:shade val="63000" /></a:schemeClr></a:gs></a:gsLst><a:lin ang="5400000" scaled="0" /></a:gradFill></a:bgFillStyleLst></a:fmtScheme></a:themeElements></a:theme>
""";
        var themePart = workbookPart.AddNewPart<ThemePart>();
        using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(themeXml));
        themePart.FeedData(stream);
    }

    private static string ColumnName(int columnNumber)
    {
        var name = string.Empty;
        while (columnNumber > 0)
        {
            columnNumber--;
            name = (char)('A' + columnNumber % 26) + name;
            columnNumber /= 26;
        }
        return name;
    }
}
