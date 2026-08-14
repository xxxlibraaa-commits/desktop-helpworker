using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using System.Globalization;
using System.IO;
using Xdr = DocumentFormat.OpenXml.Drawing.Spreadsheet;
using SpreadsheetColor = DocumentFormat.OpenXml.Spreadsheet.Color;
using SpreadsheetFont = DocumentFormat.OpenXml.Spreadsheet.Font;

namespace FloatMate.Services;

public sealed class PlanWorkbookService
{
    private const uint TitleStyle = 1;
    private const uint HeaderStyle = 2;
    private const uint BodyStyle = 3;
    private const uint DateStyle = 4;
    private const uint PercentageStyle = 5;
    private const uint CenterStyle = 6;
    private const uint TimelineStyle = 7;
    private const uint TimelineCompletedStyle = 8;
    private const uint SoftHeaderStyle = 9;
    private const uint WrappedBodyStyle = 10;

    public LongPlan Import(string path)
    {
        using var document = SpreadsheetDocument.Open(path, false);
        var workbookPart = document.WorkbookPart ?? throw new InvalidDataException("工作簿缺少 WorkbookPart。");
        var workbook = workbookPart.Workbook ?? throw new InvalidDataException("工作簿缺少 Workbook。");
        var sheets = workbook.Sheets?.Elements<Sheet>().ToList() ?? [];
        if (sheets.Count == 0) throw new InvalidDataException("工作簿中没有可读取的工作表。");

        foreach (var sheet in sheets.OrderByDescending(sheet => string.Equals(sheet.Name?.Value, "任务数据", StringComparison.OrdinalIgnoreCase)))
        {
            if (sheet.Id?.Value is not string relationId) continue;
            var worksheetPart = (WorksheetPart)workbookPart.GetPartById(relationId);
            var generic = TryImportTaskTable(workbookPart, worksheetPart, path);
            if (generic is not null) return generic;
        }

        foreach (var sheet in sheets)
        {
            if (sheet.Id?.Value is not string relationId) continue;
            var worksheetPart = (WorksheetPart)workbookPart.GetPartById(relationId);
            var template = TryImportTimelineTemplate(workbookPart, worksheetPart, path);
            if (template is not null) return template;
        }

        throw new InvalidDataException("没有找到可识别的任务表。请保留任务名称、日期表头或开始/结束日期列。");
    }

    public void Export(string path, LongPlan plan)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);

        using var document = SpreadsheetDocument.Create(path, SpreadsheetDocumentType.Workbook);
        var workbookPart = document.AddWorkbookPart();
        workbookPart.Workbook = new Workbook();
        AddCompatibilityParts(workbookPart);
        var stylesPart = workbookPart.AddNewPart<WorkbookStylesPart>();
        stylesPart.Stylesheet = CreateStylesheet();
        stylesPart.Stylesheet.Save();

        var sheets = workbookPart.Workbook.AppendChild(new Sheets());
        AddTimelineSheet(workbookPart, sheets, plan);
        AddTaskDataSheet(workbookPart, sheets, plan);
        workbookPart.Workbook.CalculationProperties = new CalculationProperties
        {
            CalculationId = 191029U,
            FullCalculationOnLoad = true,
            ForceFullCalculation = true
        };
        workbookPart.Workbook.Save();
    }

    private static LongPlan? TryImportTaskTable(WorkbookPart workbookPart, WorksheetPart worksheetPart, string path)
    {
        var worksheet = worksheetPart.Worksheet;
        if (worksheet is null) return null;
        var rows = worksheet.GetFirstChild<SheetData>()?.Elements<Row>().ToList() ?? [];
        foreach (var headerRow in rows.Take(12))
        {
            var headers = RowValues(workbookPart, headerRow);
            var titleIndex = FindHeader(headers, "任务", "工作内容", "任务名称");
            var startIndex = FindHeader(headers, "开始日期", "开始");
            var endIndex = FindHeader(headers, "结束日期", "结束");
            if (titleIndex < 0 || startIndex < 0 || endIndex < 0) continue;

            var categoryIndex = FindHeader(headers, "分类", "阶段");
            var ownerIndex = FindHeader(headers, "负责人", "责任人");
            var statusIndex = FindHeader(headers, "状态");
            var progressIndex = FindHeader(headers, "进度");
            var notesIndex = FindHeader(headers, "备注", "说明");
            var milestoneIndex = FindHeader(headers, "里程碑");
            var nameIndex = FindHeader(headers, "计划名称");
            var planStartIndex = FindHeader(headers, "计划开始", "计划开始日期");
            var planEndIndex = FindHeader(headers, "计划结束", "计划结束日期");
            var tasks = new List<LongPlanTask>();
            var planName = Path.GetFileNameWithoutExtension(path);
            DateTime? explicitPlanStart = null;
            DateTime? explicitPlanEnd = null;

            foreach (var row in rows.Where(row => row.RowIndex?.Value > headerRow.RowIndex?.Value))
            {
                var values = RowValues(workbookPart, row);
                var title = ValueAt(values, titleIndex);
                if (string.IsNullOrWhiteSpace(title)) continue;
                if (nameIndex >= 0 && !string.IsNullOrWhiteSpace(ValueAt(values, nameIndex))) planName = ValueAt(values, nameIndex);
                explicitPlanStart ??= ParseDate(ValueAt(values, planStartIndex));
                explicitPlanEnd ??= ParseDate(ValueAt(values, planEndIndex));
                var start = ParseDate(ValueAt(values, startIndex));
                var end = ParseDate(ValueAt(values, endIndex));
                var progress = ParseProgress(ValueAt(values, progressIndex));
                var status = NormalizeStatus(ValueAt(values, statusIndex), progress);
                tasks.Add(new LongPlanTask
                {
                    Order = tasks.Count + 1,
                    Category = ValueAt(values, categoryIndex),
                    Title = title,
                    Owner = ValueAt(values, ownerIndex),
                    Status = status,
                    Progress = progress,
                    StartDate = start,
                    EndDate = end,
                    Notes = ValueAt(values, notesIndex),
                    Milestone = ValueAt(values, milestoneIndex)
                });
            }

            if (tasks.Count == 0) continue;
            var scheduled = tasks.Where(task => task.StartDate.HasValue && task.EndDate.HasValue).ToList();
            var startDate = explicitPlanStart ?? (scheduled.Count == 0 ? DateTime.Today : scheduled.Min(task => task.StartDate!.Value));
            var endDate = explicitPlanEnd ?? (scheduled.Count == 0 ? DateTime.Today.AddDays(39) : scheduled.Max(task => task.EndDate!.Value));
            return new LongPlan
            {
                Name = planName,
                StartDate = startDate,
                EndDate = endDate < startDate ? startDate.AddDays(39) : endDate,
                SourceFileName = Path.GetFileName(path),
                Tasks = new(tasks)
            };
        }
        return null;
    }

    private static LongPlan? TryImportTimelineTemplate(WorkbookPart workbookPart, WorksheetPart worksheetPart, string path)
    {
        var worksheet = worksheetPart.Worksheet;
        if (worksheet is null) return null;
        var rows = worksheet.GetFirstChild<SheetData>()?.Elements<Row>().ToList() ?? [];
        if (rows.Count < 2) return null;
        var headerRow = rows.First();
        var headerCells = CellsByColumn(workbookPart, headerRow);
        var dateColumns = headerCells
            .Select(pair => (Column: pair.Key, Date: ParseDate(pair.Value)))
            .Where(pair => pair.Column >= 4 && pair.Date.HasValue)
            .ToDictionary(pair => pair.Column, pair => pair.Date!.Value.Date);
        if (dateColumns.Count < 2) return null;

        var lineRanges = ReadTimelineLines(worksheetPart, dateColumns);
        var tasks = new List<LongPlanTask>();
        var category = "未分类";
        foreach (var row in rows.Skip(1))
        {
            var rowIndex = checked((int)(row.RowIndex?.Value ?? 0U)) - 1;
            var values = CellsByColumn(workbookPart, row);
            var rowCategory = ValueAt(values, 0);
            if (!string.IsNullOrWhiteSpace(rowCategory)) category = rowCategory;
            var title = ValueAt(values, 1);
            if (string.IsNullOrWhiteSpace(title)) continue;

            lineRanges.TryGetValue(rowIndex, out var schedule);
            var timelineText = values.Where(pair => pair.Key >= 4 && !string.IsNullOrWhiteSpace(pair.Value)).Select(pair => pair.Value.Trim()).ToList();
            var completed = timelineText.Any(value => value.Contains("已完成", StringComparison.OrdinalIgnoreCase));
            var milestones = timelineText.Where(value => !value.Equals("已完成", StringComparison.OrdinalIgnoreCase)).Distinct().ToList();
            tasks.Add(new LongPlanTask
            {
                Order = tasks.Count + 1,
                Category = category,
                Title = title,
                Owner = ValueAt(values, 2),
                Notes = ValueAt(values, 3),
                StartDate = schedule.Start,
                EndDate = schedule.End,
                Status = completed ? "已完成" : schedule.Start.HasValue ? "未开始" : "未开始",
                Progress = completed ? 100 : 0,
                Milestone = string.Join("；", milestones)
            });
        }

        if (tasks.Count == 0) return null;
        return new LongPlan
        {
            Name = Path.GetFileNameWithoutExtension(path),
            StartDate = dateColumns.Values.Min(),
            EndDate = dateColumns.Values.Max(),
            SourceFileName = Path.GetFileName(path),
            Tasks = new(tasks)
        };
    }

    private static Dictionary<int, (DateTime? Start, DateTime? End)> ReadTimelineLines(WorksheetPart worksheetPart, IReadOnlyDictionary<int, DateTime> dateColumns)
    {
        var result = new Dictionary<int, (DateTime? Start, DateTime? End)>();
        var drawing = worksheetPart.DrawingsPart?.WorksheetDrawing;
        if (drawing is null) return result;
        foreach (var anchor in drawing.Elements<Xdr.TwoCellAnchor>())
        {
            if (anchor.GetFirstChild<Xdr.ConnectionShape>() is null) continue;
            var from = anchor.GetFirstChild<Xdr.FromMarker>();
            var to = anchor.GetFirstChild<Xdr.ToMarker>();
            if (from?.ColumnId?.Text is not string fromColumnText || from.RowId?.Text is not string fromRowText ||
                to?.ColumnId?.Text is not string toColumnText || to.RowId?.Text is not string toRowText ||
                !int.TryParse(fromColumnText, out var fromColumn) || !int.TryParse(toColumnText, out var toColumn) ||
                !int.TryParse(fromRowText, out var fromRow) || !int.TryParse(toRowText, out var toRow) || fromRow != toRow) continue;
            if (!dateColumns.TryGetValue(fromColumn, out var start) || !dateColumns.TryGetValue(toColumn, out var end)) continue;
            if (end < start) (start, end) = (end, start);
            result[fromRow] = (start, end);
        }
        return result;
    }

    private static void AddTimelineSheet(WorkbookPart workbookPart, Sheets sheets, LongPlan plan)
    {
        var worksheetPart = workbookPart.AddNewPart<WorksheetPart>();
        var spanDays = Math.Max(1, (plan.EndDate.Date - plan.StartDate.Date).Days + 1);
        var intervalDays = spanDays <= 62 ? 1 : 7;
        var timelineDates = Enumerable.Range(0, (int)Math.Ceiling(spanDays / (double)intervalDays))
            .Select(index => plan.StartDate.Date.AddDays(index * intervalDays)).ToList();
        var finalColumn = 9 + timelineDates.Count;
        var sheetData = new SheetData();
        worksheetPart.Worksheet = CreateWorksheet(
            [6d, 13d, 31d, 18d, 12d, 11d, 13d, 13d, 28d, .. timelineDates.Select(_ => intervalDays == 1 ? 4.2d : 8.2d)],
            sheetData, 4, "A5");

        var titleRow = CreateRow(30, TextCell(plan.Name, TitleStyle));
        sheetData.Append(titleRow);
        sheetData.Append(CreateRow(23,
            TextCell($"周期  {plan.StartDate:yyyy-MM-dd} — {plan.EndDate:yyyy-MM-dd}  ·  {spanDays} 天  ·  {plan.Tasks.Count} 项任务", BodyStyle)));
        sheetData.Append(new Row { Height = 8D, CustomHeight = true });

        var headers = new List<Cell>
        {
            TextCell("序号", HeaderStyle), TextCell("分类", HeaderStyle), TextCell("工作内容", HeaderStyle), TextCell("负责人", HeaderStyle),
            TextCell("状态", HeaderStyle), TextCell("进度", HeaderStyle), TextCell("开始日期", HeaderStyle), TextCell("结束日期", HeaderStyle), TextCell("备注 / 里程碑", HeaderStyle)
        };
        headers.AddRange(timelineDates.Select(date => DateCell(date, SoftHeaderStyle)));
        sheetData.Append(CreateRow(28, headers.ToArray()));

        foreach (var task in plan.Tasks.OrderBy(task => task.Order))
        {
            var rowCells = new List<Cell>
            {
                NumberCell(task.Order, CenterStyle), TextCell(task.Category, BodyStyle), TextCell(task.Title, WrappedBodyStyle), TextCell(task.Owner, BodyStyle),
                TextCell(task.Status, CenterStyle), NumberCell(task.Progress / 100D, PercentageStyle),
                task.StartDate is DateTime start ? DateCell(start, DateStyle) : TextCell(string.Empty, BodyStyle),
                task.EndDate is DateTime end ? DateCell(end, DateStyle) : TextCell(string.Empty, BodyStyle),
                TextCell(string.Join(" · ", new[] { task.Notes, task.Milestone }.Where(value => !string.IsNullOrWhiteSpace(value))), WrappedBodyStyle)
            };
            foreach (var date in timelineDates)
            {
                var segmentEnd = date.AddDays(intervalDays - 1);
                var active = task.StartDate.HasValue && task.EndDate.HasValue && task.StartDate.Value.Date <= segmentEnd && task.EndDate.Value.Date >= date;
                var style = !active ? BodyStyle : task.Status == "已完成" ? TimelineCompletedStyle : TimelineStyle;
                rowCells.Add(TextCell(active ? "" : string.Empty, style));
            }
            sheetData.Append(CreateRow(Math.Max(25D, 9D + Math.Ceiling(Math.Max(task.Title.Length, task.Notes.Length) / 24D) * 16D), rowCells.ToArray()));
        }
        AssignCellReferences(sheetData);

        var merges = new MergeCells(
            new MergeCell { Reference = $"A1:{ColumnName(finalColumn)}1" },
            new MergeCell { Reference = $"A2:{ColumnName(finalColumn)}2" });
        var autoFilter = new AutoFilter { Reference = $"A4:{ColumnName(finalColumn)}{plan.Tasks.Count + 4}" };
        worksheetPart.Worksheet.InsertAfter(autoFilter, sheetData);
        worksheetPart.Worksheet.InsertAfter(merges, autoFilter);
        AddStatusValidation(worksheetPart.Worksheet, $"E5:E{Math.Max(5, plan.Tasks.Count + 4)}");
        worksheetPart.Worksheet.Save();
        sheets.Append(new Sheet { Id = workbookPart.GetIdOfPart(worksheetPart), SheetId = 1U, Name = "计划进度" });
    }

    private static void AddTaskDataSheet(WorkbookPart workbookPart, Sheets sheets, LongPlan plan)
    {
        var worksheetPart = workbookPart.AddNewPart<WorksheetPart>();
        var sheetData = new SheetData();
        worksheetPart.Worksheet = CreateWorksheet([24d, 13d, 13d, 8d, 14d, 32d, 18d, 12d, 11d, 13d, 13d, 28d, 24d], sheetData, 1, "A2");
        sheetData.Append(CreateRow(28,
            TextCell("计划名称", HeaderStyle), TextCell("计划开始", HeaderStyle), TextCell("计划结束", HeaderStyle), TextCell("序号", HeaderStyle),
            TextCell("分类", HeaderStyle), TextCell("任务", HeaderStyle), TextCell("负责人", HeaderStyle), TextCell("状态", HeaderStyle),
            TextCell("进度", HeaderStyle), TextCell("开始日期", HeaderStyle), TextCell("结束日期", HeaderStyle), TextCell("备注", HeaderStyle), TextCell("里程碑", HeaderStyle)));
        foreach (var task in plan.Tasks.OrderBy(task => task.Order))
        {
            sheetData.Append(CreateRow(25,
                TextCell(plan.Name, BodyStyle), DateCell(plan.StartDate, DateStyle), DateCell(plan.EndDate, DateStyle), NumberCell(task.Order, CenterStyle),
                TextCell(task.Category, BodyStyle), TextCell(task.Title, WrappedBodyStyle), TextCell(task.Owner, BodyStyle), TextCell(task.Status, CenterStyle), NumberCell(task.Progress / 100D, PercentageStyle),
                task.StartDate is DateTime start ? DateCell(start, DateStyle) : TextCell(string.Empty, BodyStyle),
                task.EndDate is DateTime end ? DateCell(end, DateStyle) : TextCell(string.Empty, BodyStyle),
                TextCell(task.Notes, WrappedBodyStyle), TextCell(task.Milestone, WrappedBodyStyle)));
        }
        AssignCellReferences(sheetData);
        worksheetPart.Worksheet.InsertAfter(new AutoFilter { Reference = $"A1:M{Math.Max(1, plan.Tasks.Count + 1)}" }, sheetData);
        AddStatusValidation(worksheetPart.Worksheet, $"H2:H{Math.Max(2, plan.Tasks.Count + 1)}");
        worksheetPart.Worksheet.Save();
        sheets.Append(new Sheet { Id = workbookPart.GetIdOfPart(worksheetPart), SheetId = 2U, Name = "任务数据" });
    }

    private static Worksheet CreateWorksheet(IReadOnlyList<double> widths, SheetData sheetData, int frozenRows, string topLeftCell)
    {
        var columns = new Columns();
        for (var index = 0; index < widths.Count; index++)
        {
            columns.Append(new Column { Min = (uint)index + 1, Max = (uint)index + 1, Width = widths[index], CustomWidth = true });
        }
        return new Worksheet(
            new SheetViews(new SheetView(new Pane
            {
                VerticalSplit = frozenRows,
                TopLeftCell = topLeftCell,
                ActivePane = PaneValues.BottomLeft,
                State = PaneStateValues.Frozen
            }) { WorkbookViewId = 0U, ShowGridLines = false }),
            new SheetFormatProperties { DefaultRowHeight = 21D }, columns, sheetData);
    }

    private static void AddStatusValidation(Worksheet worksheet, string range)
    {
        var validations = new DataValidations { Count = 1U };
        validations.Append(new DataValidation(new Formula1("\"未开始,进行中,已完成,暂停\""))
        {
            Type = DataValidationValues.List,
            AllowBlank = true,
            SequenceOfReferences = new ListValue<StringValue> { InnerText = range }
        });
        worksheet.Append(validations);
    }

    private static Dictionary<int, string> CellsByColumn(WorkbookPart workbookPart, Row row)
    {
        var result = new Dictionary<int, string>();
        var nextColumn = 0;
        foreach (var cell in row.Elements<Cell>())
        {
            var column = cell.CellReference?.Value is string reference ? ColumnIndex(reference) : nextColumn;
            result[column] = CellText(workbookPart, cell);
            nextColumn = column + 1;
        }
        return result;
    }

    private static List<string> RowValues(WorkbookPart workbookPart, Row row)
    {
        var byColumn = CellsByColumn(workbookPart, row);
        if (byColumn.Count == 0) return [];
        var result = Enumerable.Repeat(string.Empty, byColumn.Keys.Max() + 1).ToList();
        foreach (var pair in byColumn) result[pair.Key] = pair.Value;
        return result;
    }

    private static string CellText(WorkbookPart workbookPart, Cell cell)
    {
        if (cell.DataType?.Value == CellValues.SharedString && int.TryParse(cell.CellValue?.Text, out var index))
            return workbookPart.SharedStringTablePart?.SharedStringTable?.ElementAtOrDefault(index)?.InnerText ?? string.Empty;
        if (cell.DataType?.Value == CellValues.InlineString) return cell.InlineString?.InnerText ?? string.Empty;
        if (cell.DataType?.Value == CellValues.Boolean) return cell.CellValue?.Text == "1" ? "TRUE" : "FALSE";
        return cell.CellValue?.Text ?? cell.InnerText ?? string.Empty;
    }

    private static int ColumnIndex(string reference)
    {
        var value = 0;
        foreach (var character in reference.TakeWhile(char.IsLetter)) value = value * 26 + char.ToUpperInvariant(character) - 'A' + 1;
        return Math.Max(0, value - 1);
    }

    private static int FindHeader(IReadOnlyList<string> headers, params string[] candidates)
    {
        for (var index = 0; index < headers.Count; index++)
        {
            if (candidates.Any(candidate => headers[index].Trim().Equals(candidate, StringComparison.OrdinalIgnoreCase))) return index;
        }
        return -1;
    }

    private static string ValueAt(IReadOnlyList<string> values, int index) => index >= 0 && index < values.Count ? values[index].Trim() : string.Empty;
    private static string ValueAt(IReadOnlyDictionary<int, string> values, int index) => values.TryGetValue(index, out var value) ? value.Trim() : string.Empty;

    private static DateTime? ParseDate(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var serial) && serial is > 1 and < 2958466)
        {
            try { return DateTime.FromOADate(serial).Date; } catch { }
        }
        return DateTime.TryParse(value, CultureInfo.CurrentCulture, DateTimeStyles.AllowWhiteSpaces, out var date) ||
               DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out date)
            ? date.Date : null;
    }

    private static int ParseProgress(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return 0;
        var normalized = value.Trim().TrimEnd('%');
        if (!double.TryParse(normalized, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) &&
            !double.TryParse(normalized, NumberStyles.Float, CultureInfo.CurrentCulture, out number)) return 0;
        if (!value.Contains('%') && number <= 1D) number *= 100D;
        return Math.Clamp((int)Math.Round(number), 0, 100);
    }

    private static string NormalizeStatus(string value, int progress)
    {
        if (value.Contains("完成", StringComparison.OrdinalIgnoreCase)) return "已完成";
        if (value.Contains("进行", StringComparison.OrdinalIgnoreCase)) return "进行中";
        if (value.Contains("暂停", StringComparison.OrdinalIgnoreCase)) return "暂停";
        return progress >= 100 ? "已完成" : progress > 0 ? "进行中" : "未开始";
    }

    private static Row CreateRow(double height, params Cell[] cells)
    {
        var row = new Row { Height = height, CustomHeight = true };
        row.Append(cells);
        return row;
    }

    private static void AssignCellReferences(SheetData sheetData)
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

    private static Cell TextCell(string value, uint styleIndex) => new()
    {
        DataType = CellValues.String,
        StyleIndex = styleIndex,
        CellValue = new CellValue(value ?? string.Empty)
    };

    private static Cell NumberCell(double value, uint styleIndex) => new()
    {
        DataType = CellValues.Number,
        StyleIndex = styleIndex,
        CellValue = new CellValue(value.ToString(CultureInfo.InvariantCulture))
    };

    private static Cell DateCell(DateTime value, uint styleIndex) => new()
    {
        DataType = CellValues.Number,
        StyleIndex = styleIndex,
        CellValue = new CellValue(value.ToOADate().ToString(CultureInfo.InvariantCulture))
    };

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
        const uint dateFormatId = 164;
        const uint percentageFormatId = 165;
        const uint timelineDateFormatId = 166;
        var fonts = new Fonts(
            new SpreadsheetFont(new FontSize { Val = 10.5D }, new SpreadsheetColor { Rgb = "FF1C1C1E" }, new FontName { Val = "Microsoft YaHei UI" }),
            new SpreadsheetFont(new Bold(), new FontSize { Val = 11D }, new SpreadsheetColor { Rgb = "FFFFFFFF" }, new FontName { Val = "Microsoft YaHei UI" }),
            new SpreadsheetFont(new Bold(), new FontSize { Val = 16D }, new SpreadsheetColor { Rgb = "FF1C1C1E" }, new FontName { Val = "Microsoft YaHei UI" })) { Count = 3U };
        var fills = new Fills(
            new Fill(new PatternFill { PatternType = PatternValues.None }),
            new Fill(new PatternFill { PatternType = PatternValues.Gray125 }),
            SolidFill("FF0066CC"), SolidFill("FFF2F2F7"), SolidFill("FFE5F1FF"), SolidFill("FFE8F7EC")) { Count = 6U };
        var borders = new Borders(
            new Border(),
            new Border { BottomBorder = new BottomBorder { Style = BorderStyleValues.Thin, Color = new SpreadsheetColor { Rgb = "FFD7D7DC" } } },
            new Border { BottomBorder = new BottomBorder { Style = BorderStyleValues.Thin, Color = new SpreadsheetColor { Rgb = "FFE3E3E8" } } },
            new Border
            {
                LeftBorder = new LeftBorder { Style = BorderStyleValues.Thin, Color = new SpreadsheetColor { Rgb = "FFFFFFFF" } },
                RightBorder = new RightBorder { Style = BorderStyleValues.Thin, Color = new SpreadsheetColor { Rgb = "FFFFFFFF" } }
            }) { Count = 4U };
        var formats = new CellFormats(
            new CellFormat(),
            new CellFormat { FontId = 2U, ApplyFont = true, ApplyAlignment = true, Alignment = new Alignment { Vertical = VerticalAlignmentValues.Center } },
            new CellFormat { FontId = 1U, FillId = 2U, BorderId = 1U, ApplyFont = true, ApplyFill = true, ApplyBorder = true, ApplyAlignment = true, Alignment = new Alignment { Horizontal = HorizontalAlignmentValues.Center, Vertical = VerticalAlignmentValues.Center, WrapText = true } },
            new CellFormat { FontId = 0U, BorderId = 2U, ApplyFont = true, ApplyBorder = true, ApplyAlignment = true, Alignment = new Alignment { Vertical = VerticalAlignmentValues.Center } },
            new CellFormat { FontId = 0U, BorderId = 2U, NumberFormatId = dateFormatId, ApplyFont = true, ApplyBorder = true, ApplyNumberFormat = true, ApplyAlignment = true, Alignment = new Alignment { Horizontal = HorizontalAlignmentValues.Center, Vertical = VerticalAlignmentValues.Center } },
            new CellFormat { FontId = 0U, BorderId = 2U, NumberFormatId = percentageFormatId, ApplyFont = true, ApplyBorder = true, ApplyNumberFormat = true, ApplyAlignment = true, Alignment = new Alignment { Horizontal = HorizontalAlignmentValues.Right, Vertical = VerticalAlignmentValues.Center } },
            new CellFormat { FontId = 0U, BorderId = 2U, ApplyFont = true, ApplyBorder = true, ApplyAlignment = true, Alignment = new Alignment { Horizontal = HorizontalAlignmentValues.Center, Vertical = VerticalAlignmentValues.Center } },
            new CellFormat { FontId = 0U, FillId = 4U, BorderId = 3U, ApplyFont = true, ApplyFill = true, ApplyBorder = true },
            new CellFormat { FontId = 0U, FillId = 5U, BorderId = 3U, ApplyFont = true, ApplyFill = true, ApplyBorder = true },
            new CellFormat { FontId = 0U, FillId = 3U, BorderId = 1U, NumberFormatId = timelineDateFormatId, ApplyFont = true, ApplyFill = true, ApplyBorder = true, ApplyNumberFormat = true, ApplyAlignment = true, Alignment = new Alignment { Horizontal = HorizontalAlignmentValues.Center, Vertical = VerticalAlignmentValues.Center } },
            new CellFormat { FontId = 0U, BorderId = 2U, ApplyFont = true, ApplyBorder = true, ApplyAlignment = true, Alignment = new Alignment { Vertical = VerticalAlignmentValues.Top, WrapText = true } }) { Count = 11U };
        return new Stylesheet(
            new NumberingFormats(
                new NumberingFormat { NumberFormatId = dateFormatId, FormatCode = "yyyy-mm-dd" },
                new NumberingFormat { NumberFormatId = percentageFormatId, FormatCode = "0%" },
                new NumberingFormat { NumberFormatId = timelineDateFormatId, FormatCode = "m/d" }) { Count = 3U },
            fonts, fills, borders, new CellStyleFormats(new CellFormat()) { Count = 1U }, formats,
            new CellStyles(new CellStyle { Name = "Normal", FormatId = 0U, BuiltinId = 0U }) { Count = 1U });
    }

    private static Fill SolidFill(string color) => new(new PatternFill(
        new ForegroundColor { Rgb = color }, new BackgroundColor { Indexed = 64U }) { PatternType = PatternValues.Solid });

    private static void AddCompatibilityParts(WorkbookPart workbookPart)
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
}
