using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using System.Globalization;
using System.IO;
using SpreadsheetColor = DocumentFormat.OpenXml.Spreadsheet.Color;
using SpreadsheetFont = DocumentFormat.OpenXml.Spreadsheet.Font;

namespace FloatMate.Services;

public sealed class XlsmExportService
{
    private const uint HeaderStyle = 1;
    private const uint BodyStyle = 2;
    private const uint DateTimeStyle = 3;
    private const uint PercentageStyle = 4;
    private const uint IntegerStyle = 5;
    private const uint DecimalStyle = 6;
    private const uint DateOnlyStyle = 7;
    private const uint CenterBodyStyle = 8;
    private const uint WrappedBodyStyle = 9;

    public void ExportWorkToday(string path, DateOnly date, IReadOnlyCollection<GoalItem> goals)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);

        using var document = SpreadsheetDocument.Create(path, SpreadsheetDocumentType.MacroEnabledWorkbook);
        var workbookPart = document.AddWorkbookPart();
        workbookPart.Workbook = new Workbook();
        SpreadsheetExportCompatibility.Add(workbookPart);

        var stylesPart = workbookPart.AddNewPart<WorkbookStylesPart>();
        stylesPart.Stylesheet = CreateStylesheet();
        stylesPart.Stylesheet.Save();

        var sheets = workbookPart.Workbook.AppendChild(new Sheets());
        AddGoalsSheet(workbookPart, sheets, date, goals.OrderBy(goal => goal.CreatedAt).ToList());

        workbookPart.Workbook.CalculationProperties = new CalculationProperties
        {
            CalculationId = 191029U,
            FullCalculationOnLoad = true,
            ForceFullCalculation = true
        };
        workbookPart.Workbook.Save();
    }

    private static void AddGoalsSheet(WorkbookPart workbookPart, Sheets sheets, DateOnly date, IReadOnlyList<GoalItem> goals)
    {
        var worksheetPart = workbookPart.AddNewPart<WorksheetPart>();
        var sheetData = new SheetData();
        worksheetPart.Worksheet = CreateWorksheet(
            new[] { 13d, 28d, 48d, 12d, 11d, 17d, 17d, 21d, 21d },
            sheetData);

        sheetData.Append(CreateRow(new[]
        {
            TextCell("日期", HeaderStyle), TextCell("任务名称", HeaderStyle), TextCell("详细工作内容", HeaderStyle), TextCell("状态", HeaderStyle),
            TextCell("进度", HeaderStyle), TextCell("预计时长（分钟）", HeaderStyle), TextCell("专注时长（分钟）", HeaderStyle),
            TextCell("创建时间", HeaderStyle), TextCell("完成时间", HeaderStyle)
        }, 26));

        foreach (var goal in goals)
        {
            sheetData.Append(CreateRow(new[]
            {
                DateOnlyCell(date),
                TextCell(goal.Title, BodyStyle),
                TextCell(goal.Details, WrappedBodyStyle),
                TextCell(goal.StatusLabel, CenterBodyStyle),
                NumberCell(goal.Progress / 100d, PercentageStyle),
                NumberCell(goal.EstimateMinutes, IntegerStyle),
                NumberCell(goal.FocusSeconds / 60d, DecimalStyle),
                DateCell(goal.CreatedAt),
                goal.CompletedAt is DateTime completedAt ? DateCell(completedAt) : TextCell(string.Empty, BodyStyle)
            }, CalculateGoalRowHeight(goal.Details)));
        }

        SpreadsheetExportCompatibility.AssignCellReferences(sheetData);
        ApplyAutoFilter(worksheetPart.Worksheet, 9, goals.Count + 1);
        worksheetPart.Worksheet.Save();
        sheets.Append(new Sheet
        {
            Id = workbookPart.GetIdOfPart(worksheetPart),
            SheetId = 1U,
            Name = "今日任务"
        });
    }

    private static Worksheet CreateWorksheet(IReadOnlyList<double> widths, SheetData sheetData)
    {
        var columns = new Columns();
        for (var index = 0; index < widths.Count; index++)
        {
            columns.Append(new Column
            {
                Min = (uint)index + 1,
                Max = (uint)index + 1,
                Width = widths[index],
                CustomWidth = true
            });
        }

        return new Worksheet(
            new SheetViews(
                new SheetView(
                    new Pane
                    {
                        VerticalSplit = 1D,
                        TopLeftCell = "A2",
                        ActivePane = PaneValues.BottomLeft,
                        State = PaneStateValues.Frozen
                    })
                {
                    WorkbookViewId = 0U,
                    ShowGridLines = false
                }),
            new SheetFormatProperties { DefaultRowHeight = 21D },
            columns,
            sheetData);
    }

    private static void ApplyAutoFilter(Worksheet worksheet, int columnCount, int rowCount)
    {
        var finalCell = $"{ColumnName(columnCount)}{Math.Max(rowCount, 1)}";
        worksheet.InsertAfter(new AutoFilter { Reference = $"A1:{finalCell}" }, worksheet.GetFirstChild<SheetData>());
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
        StyleIndex = DateTimeStyle,
        CellValue = new CellValue(value.ToOADate().ToString(CultureInfo.InvariantCulture))
    };

    private static Cell DateOnlyCell(DateOnly value) => new()
    {
        DataType = CellValues.Number,
        StyleIndex = DateOnlyStyle,
        CellValue = new CellValue(value.ToDateTime(TimeOnly.MinValue).ToOADate().ToString(CultureInfo.InvariantCulture))
    };

    private static double CalculateGoalRowHeight(string details)
    {
        if (string.IsNullOrWhiteSpace(details)) return 23D;
        var estimatedLines = details.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Sum(line => Math.Max(1, (int)Math.Ceiling(line.Length / 32D)));
        return Math.Clamp(8D + estimatedLines * 18D, 42D, 300D);
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

    private static Stylesheet CreateStylesheet()
    {
        const uint dateTimeFormatId = 164;
        const uint percentageFormatId = 165;
        const uint decimalFormatId = 166;
        const uint dateOnlyFormatId = 167;

        var fonts = new Fonts(
            new SpreadsheetFont(new FontName { Val = "Microsoft YaHei UI" }, new FontSize { Val = 11D }, new SpreadsheetColor { Rgb = "FF1D1D1F" }),
            new SpreadsheetFont(new Bold(), new FontName { Val = "Microsoft YaHei UI" }, new FontSize { Val = 11D }, new SpreadsheetColor { Rgb = "FFFFFFFF" }))
        { Count = 2U };

        var fills = new Fills(
            new Fill(new PatternFill { PatternType = PatternValues.None }),
            new Fill(new PatternFill { PatternType = PatternValues.Gray125 }),
            new Fill(new PatternFill(new ForegroundColor { Rgb = "FF0A84FF" }, new BackgroundColor { Indexed = 64U }) { PatternType = PatternValues.Solid }))
        { Count = 3U };

        var borders = new Borders(
            new Border(),
            new Border { BottomBorder = new BottomBorder { Style = BorderStyleValues.Thin, Color = new SpreadsheetColor { Rgb = "FFD7D7DB" } } },
            new Border { BottomBorder = new BottomBorder { Style = BorderStyleValues.Thin, Color = new SpreadsheetColor { Rgb = "FFE5E5EA" } } })
        { Count = 3U };

        var cellFormats = new CellFormats(
            new CellFormat(),
            new CellFormat { FontId = 1U, FillId = 2U, BorderId = 1U, ApplyFont = true, ApplyFill = true, ApplyBorder = true, ApplyAlignment = true, Alignment = new Alignment { Horizontal = HorizontalAlignmentValues.Center, Vertical = VerticalAlignmentValues.Center } },
            new CellFormat { FontId = 0U, FillId = 0U, BorderId = 2U, ApplyFont = true, ApplyBorder = true, ApplyAlignment = true, Alignment = new Alignment { Vertical = VerticalAlignmentValues.Center } },
            new CellFormat { FontId = 0U, FillId = 0U, BorderId = 2U, NumberFormatId = dateTimeFormatId, ApplyFont = true, ApplyBorder = true, ApplyNumberFormat = true, ApplyAlignment = true, Alignment = new Alignment { Vertical = VerticalAlignmentValues.Center } },
            new CellFormat { FontId = 0U, FillId = 0U, BorderId = 2U, NumberFormatId = percentageFormatId, ApplyFont = true, ApplyBorder = true, ApplyNumberFormat = true, ApplyAlignment = true, Alignment = new Alignment { Horizontal = HorizontalAlignmentValues.Right, Vertical = VerticalAlignmentValues.Center } },
            new CellFormat { FontId = 0U, FillId = 0U, BorderId = 2U, NumberFormatId = 1U, ApplyFont = true, ApplyBorder = true, ApplyNumberFormat = true, ApplyAlignment = true, Alignment = new Alignment { Horizontal = HorizontalAlignmentValues.Right, Vertical = VerticalAlignmentValues.Center } },
            new CellFormat { FontId = 0U, FillId = 0U, BorderId = 2U, NumberFormatId = decimalFormatId, ApplyFont = true, ApplyBorder = true, ApplyNumberFormat = true, ApplyAlignment = true, Alignment = new Alignment { Horizontal = HorizontalAlignmentValues.Right, Vertical = VerticalAlignmentValues.Center } },
            new CellFormat { FontId = 0U, FillId = 0U, BorderId = 2U, NumberFormatId = dateOnlyFormatId, ApplyFont = true, ApplyBorder = true, ApplyNumberFormat = true, ApplyAlignment = true, Alignment = new Alignment { Horizontal = HorizontalAlignmentValues.Center, Vertical = VerticalAlignmentValues.Center } },
            new CellFormat { FontId = 0U, FillId = 0U, BorderId = 2U, ApplyFont = true, ApplyBorder = true, ApplyAlignment = true, Alignment = new Alignment { Horizontal = HorizontalAlignmentValues.Center, Vertical = VerticalAlignmentValues.Center } },
            new CellFormat { FontId = 0U, FillId = 0U, BorderId = 2U, ApplyFont = true, ApplyBorder = true, ApplyAlignment = true, Alignment = new Alignment { Vertical = VerticalAlignmentValues.Top, WrapText = true } })
        { Count = 10U };

        return new Stylesheet(
            new NumberingFormats(
                new NumberingFormat { NumberFormatId = dateTimeFormatId, FormatCode = "yyyy-mm-dd hh:mm" },
                new NumberingFormat { NumberFormatId = percentageFormatId, FormatCode = "0%" },
                new NumberingFormat { NumberFormatId = decimalFormatId, FormatCode = "0.0" },
                new NumberingFormat { NumberFormatId = dateOnlyFormatId, FormatCode = "yyyy-mm-dd" })
            { Count = 4U },
            fonts,
            fills,
            borders,
            new CellStyleFormats(new CellFormat()) { Count = 1U },
            cellFormats,
            new CellStyles(new CellStyle { Name = "Normal", FormatId = 0U, BuiltinId = 0U }) { Count = 1U });
    }
}
