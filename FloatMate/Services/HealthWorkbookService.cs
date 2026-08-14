using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using System.Globalization;
using System.IO;
using SpreadsheetColor = DocumentFormat.OpenXml.Spreadsheet.Color;
using SpreadsheetFont = DocumentFormat.OpenXml.Spreadsheet.Font;

namespace FloatMate.Services;

public sealed class HealthWorkbookService
{
    private const uint TitleStyle = 1;
    private const uint HeaderStyle = 2;
    private const uint BodyStyle = 3;
    private const uint DateStyle = 4;
    private const uint TimeStyle = 5;
    private const uint IntegerStyle = 6;
    private const uint DecimalStyle = 7;
    private const uint CenterStyle = 8;
    private const uint WrappedStyle = 9;
    private static readonly string[] HealthTypes = ["喝水", "如厕", "起身", "护眼"];

    public void ExportToday(string path, DateOnly date, IReadOnlyCollection<QuickRecord> records)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);
        var ordered = records.OrderBy(record => record.Timestamp).ToList();

        using var document = SpreadsheetDocument.Create(path, SpreadsheetDocumentType.Workbook);
        var workbookPart = document.AddWorkbookPart();
        workbookPart.Workbook = new Workbook();
        SpreadsheetExportCompatibility.Add(workbookPart);
        var stylesPart = workbookPart.AddNewPart<WorkbookStylesPart>();
        stylesPart.Stylesheet = CreateStylesheet();
        stylesPart.Stylesheet.Save();

        var sheets = workbookPart.Workbook.AppendChild(new Sheets());
        AddSummarySheet(workbookPart, sheets, date, ordered);
        AddDetailSheet(workbookPart, sheets, ordered);
        workbookPart.Workbook.CalculationProperties = new CalculationProperties
        {
            CalculationId = 191029U,
            FullCalculationOnLoad = true,
            ForceFullCalculation = true
        };
        workbookPart.Workbook.Save();
    }

    private static void AddSummarySheet(WorkbookPart workbookPart, Sheets sheets, DateOnly date, IReadOnlyList<QuickRecord> records)
    {
        var worksheetPart = workbookPart.AddNewPart<WorksheetPart>();
        var sheetData = new SheetData();
        var worksheet = CreateWorksheet([15d, 12d, 14d, 11d, 12d, 12d, 44d, 23d], sheetData, 4, "A5");
        worksheetPart.Worksheet = worksheet;

        sheetData.Append(CreateRow([TextCell("FloatMate 健康记录汇总", TitleStyle)], 34));
        sheetData.Append(CreateRow([TextCell($"统计日期：{date:yyyy年M月d日}    健康记录总数：{records.Count} 条", BodyStyle)], 24));
        sheetData.Append(new Row { Height = 8D, CustomHeight = true });
        sheetData.Append(CreateRow([
            TextCell("类型", HeaderStyle), TextCell("总次数", HeaderStyle), TextCell("总数量", HeaderStyle), TextCell("单位", HeaderStyle),
            TextCell("首次时间", HeaderStyle), TextCell("最后时间", HeaderStyle), TextCell("发生时间", HeaderStyle), TextCell("说明", HeaderStyle)
        ], 28));

        foreach (var type in HealthTypes)
        {
            var typed = records.Where(record => record.Type == type).ToList();
            var totalAmount = typed.Sum(record => record.Amount ?? 0D);
            var unit = typed.Select(record => record.Unit).FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? string.Empty;
            var times = string.Join("、", typed.Select(record => record.Timestamp.ToString("HH:mm")));
            var note = type == "喝水" && typed.Count > 0 ? $"平均每次 {totalAmount / typed.Count:0.#} {unit}" : typed.Count == 0 ? "当日暂无记录" : "独立记录";
            sheetData.Append(CreateRow([
                TextCell(type, CenterStyle), NumberCell(typed.Count, IntegerStyle),
                typed.Any(record => record.Amount.HasValue) ? NumberCell(totalAmount, IsWholeNumber(totalAmount) ? IntegerStyle : DecimalStyle) : TextCell(string.Empty, BodyStyle),
                TextCell(unit, CenterStyle),
                typed.Count > 0 ? TimeCell(typed[0].Timestamp) : TextCell(string.Empty, BodyStyle),
                typed.Count > 0 ? TimeCell(typed[^1].Timestamp) : TextCell(string.Empty, BodyStyle),
                TextCell(times, WrappedStyle), TextCell(note, WrappedStyle)
            ], Math.Max(26D, 16D + Math.Ceiling(Math.Max(1, times.Length) / 24D) * 15D)));
        }

        SpreadsheetExportCompatibility.AssignCellReferences(sheetData);
        worksheet.Append(new AutoFilter { Reference = "A4:H8" });
        worksheet.Append(new MergeCells(new MergeCell { Reference = "A1:H1" }, new MergeCell { Reference = "A2:H2" }));
        worksheet.Save();
        sheets.Append(new Sheet { Id = workbookPart.GetIdOfPart(worksheetPart), SheetId = 1U, Name = "健康汇总" });
    }

    private static void AddDetailSheet(WorkbookPart workbookPart, Sheets sheets, IReadOnlyList<QuickRecord> records)
    {
        var worksheetPart = workbookPart.AddNewPart<WorksheetPart>();
        var sheetData = new SheetData();
        var worksheet = CreateWorksheet([9d, 14d, 14d, 15d, 13d, 11d], sheetData, 1, "A2");
        worksheetPart.Worksheet = worksheet;
        sheetData.Append(CreateRow([
            TextCell("序号", HeaderStyle), TextCell("日期", HeaderStyle), TextCell("发生时间", HeaderStyle),
            TextCell("类型", HeaderStyle), TextCell("数量", HeaderStyle), TextCell("单位", HeaderStyle)
        ], 28));

        for (var index = 0; index < records.Count; index++)
        {
            var record = records[index];
            sheetData.Append(CreateRow([
                NumberCell(index + 1, IntegerStyle), DateCell(record.Timestamp), TimeCell(record.Timestamp), TextCell(record.Type, CenterStyle),
                record.Amount is double amount ? NumberCell(amount, IsWholeNumber(amount) ? IntegerStyle : DecimalStyle) : TextCell(string.Empty, BodyStyle),
                TextCell(record.Unit ?? string.Empty, CenterStyle)
            ], 24));
        }

        SpreadsheetExportCompatibility.AssignCellReferences(sheetData);
        worksheet.Append(new AutoFilter { Reference = $"A1:F{Math.Max(1, records.Count + 1)}" });
        worksheet.Save();
        sheets.Append(new Sheet { Id = workbookPart.GetIdOfPart(worksheetPart), SheetId = 2U, Name = "健康明细" });
    }

    private static Worksheet CreateWorksheet(IReadOnlyList<double> widths, SheetData sheetData, int frozenRows, string topLeftCell)
    {
        var columns = new Columns();
        for (var index = 0; index < widths.Count; index++)
            columns.Append(new Column { Min = (uint)index + 1, Max = (uint)index + 1, Width = widths[index], CustomWidth = true });
        return new Worksheet(
            new SheetViews(new SheetView(new Pane { VerticalSplit = frozenRows, TopLeftCell = topLeftCell, ActivePane = PaneValues.BottomLeft, State = PaneStateValues.Frozen })
            { WorkbookViewId = 0U, ShowGridLines = false }),
            new SheetFormatProperties { DefaultRowHeight = 22D }, columns, sheetData);
    }

    private static Row CreateRow(IEnumerable<Cell> cells, double height)
    {
        var row = new Row { Height = height, CustomHeight = true };
        row.Append(cells);
        return row;
    }

    private static Cell TextCell(string value, uint styleIndex) => new()
    {
        DataType = CellValues.InlineString,
        StyleIndex = styleIndex,
        InlineString = new InlineString(new Text(value ?? string.Empty) { Space = SpaceProcessingModeValues.Preserve })
    };

    private static Cell NumberCell(double value, uint styleIndex) => new()
    {
        DataType = CellValues.Number,
        StyleIndex = styleIndex,
        CellValue = new CellValue(value.ToString(CultureInfo.InvariantCulture))
    };

    private static Cell DateCell(DateTime value) => new()
    {
        DataType = CellValues.Number,
        StyleIndex = DateStyle,
        CellValue = new CellValue(value.ToOADate().ToString(CultureInfo.InvariantCulture))
    };

    private static Cell TimeCell(DateTime value) => new()
    {
        DataType = CellValues.Number,
        StyleIndex = TimeStyle,
        CellValue = new CellValue(value.ToOADate().ToString(CultureInfo.InvariantCulture))
    };

    private static bool IsWholeNumber(double value) => Math.Abs(value - Math.Round(value)) < 0.0000001D;

    private static Stylesheet CreateStylesheet()
    {
        const uint dateFormatId = 164;
        const uint timeFormatId = 165;
        const uint decimalFormatId = 166;
        var fonts = new Fonts(
            new SpreadsheetFont(new FontName { Val = "Microsoft YaHei UI" }, new FontSize { Val = 11D }, new SpreadsheetColor { Rgb = "FF1C1C1E" }),
            new SpreadsheetFont(new Bold(), new FontName { Val = "Microsoft YaHei UI" }, new FontSize { Val = 11D }, new SpreadsheetColor { Rgb = "FFFFFFFF" }),
            new SpreadsheetFont(new Bold(), new FontName { Val = "Microsoft YaHei UI" }, new FontSize { Val = 18D }, new SpreadsheetColor { Rgb = "FF1C1C1E" })) { Count = 3U };
        var fills = new Fills(
            new Fill(new PatternFill { PatternType = PatternValues.None }),
            new Fill(new PatternFill { PatternType = PatternValues.Gray125 }),
            new Fill(new PatternFill(new ForegroundColor { Rgb = "FF23415F" }, new BackgroundColor { Indexed = 64U }) { PatternType = PatternValues.Solid })) { Count = 3U };
        var borders = new Borders(
            new Border(),
            new Border { BottomBorder = new BottomBorder { Style = BorderStyleValues.Medium, Color = new SpreadsheetColor { Rgb = "FF007AFF" } } },
            new Border { BottomBorder = new BottomBorder { Style = BorderStyleValues.Thin, Color = new SpreadsheetColor { Rgb = "FFE3E3E8" } } }) { Count = 3U };
        var cellFormats = new CellFormats(
            new CellFormat(),
            new CellFormat { FontId = 2U, ApplyFont = true, ApplyAlignment = true, Alignment = new Alignment { Vertical = VerticalAlignmentValues.Center } },
            new CellFormat { FontId = 1U, FillId = 2U, BorderId = 1U, ApplyFont = true, ApplyFill = true, ApplyBorder = true, ApplyAlignment = true, Alignment = new Alignment { Horizontal = HorizontalAlignmentValues.Center, Vertical = VerticalAlignmentValues.Center, WrapText = true } },
            new CellFormat { FontId = 0U, BorderId = 2U, ApplyFont = true, ApplyBorder = true, ApplyAlignment = true, Alignment = new Alignment { Vertical = VerticalAlignmentValues.Center } },
            new CellFormat { FontId = 0U, BorderId = 2U, NumberFormatId = dateFormatId, ApplyFont = true, ApplyBorder = true, ApplyNumberFormat = true, ApplyAlignment = true, Alignment = new Alignment { Horizontal = HorizontalAlignmentValues.Center, Vertical = VerticalAlignmentValues.Center } },
            new CellFormat { FontId = 0U, BorderId = 2U, NumberFormatId = timeFormatId, ApplyFont = true, ApplyBorder = true, ApplyNumberFormat = true, ApplyAlignment = true, Alignment = new Alignment { Horizontal = HorizontalAlignmentValues.Center, Vertical = VerticalAlignmentValues.Center } },
            new CellFormat { FontId = 0U, BorderId = 2U, NumberFormatId = 1U, ApplyFont = true, ApplyBorder = true, ApplyNumberFormat = true, ApplyAlignment = true, Alignment = new Alignment { Horizontal = HorizontalAlignmentValues.Right, Vertical = VerticalAlignmentValues.Center } },
            new CellFormat { FontId = 0U, BorderId = 2U, NumberFormatId = decimalFormatId, ApplyFont = true, ApplyBorder = true, ApplyNumberFormat = true, ApplyAlignment = true, Alignment = new Alignment { Horizontal = HorizontalAlignmentValues.Right, Vertical = VerticalAlignmentValues.Center } },
            new CellFormat { FontId = 0U, BorderId = 2U, ApplyFont = true, ApplyBorder = true, ApplyAlignment = true, Alignment = new Alignment { Horizontal = HorizontalAlignmentValues.Center, Vertical = VerticalAlignmentValues.Center } },
            new CellFormat { FontId = 0U, BorderId = 2U, ApplyFont = true, ApplyBorder = true, ApplyAlignment = true, Alignment = new Alignment { Vertical = VerticalAlignmentValues.Top, WrapText = true } }) { Count = 10U };
        return new Stylesheet(
            new NumberingFormats(
                new NumberingFormat { NumberFormatId = dateFormatId, FormatCode = "yyyy-mm-dd" },
                new NumberingFormat { NumberFormatId = timeFormatId, FormatCode = "hh:mm" },
                new NumberingFormat { NumberFormatId = decimalFormatId, FormatCode = "0.0" }) { Count = 3U },
            fonts, fills, borders, new CellStyleFormats(new CellFormat()) { Count = 1U }, cellFormats,
            new CellStyles(new CellStyle { Name = "Normal", FormatId = 0U, BuiltinId = 0U }) { Count = 1U });
    }
}
