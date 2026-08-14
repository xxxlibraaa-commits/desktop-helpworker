using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using W = DocumentFormat.OpenXml.Wordprocessing;
using System.IO;

namespace FloatMate.Services;

public sealed class WorkDocxExportService
{
    private const string BodyFont = "Microsoft YaHei UI";
    private const string Ink = "1C1C1E";
    private const string Muted = "5A5A60";
    private const string Accent = "2E74B5";
    private const string SoftFill = "F2F4F7";
    private const string Border = "D9DEE5";
    private static readonly int[] TaskTableWidths = [600, 3600, 1050, 900, 1500, 1710];

    public void ExportToday(string path, DateOnly date, IReadOnlyCollection<GoalItem> goals)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);
        var ordered = goals.OrderBy(goal => goal.CreatedAt).ToList();

        using var document = WordprocessingDocument.Create(path, WordprocessingDocumentType.Document);
        var mainPart = document.AddMainDocumentPart();
        mainPart.Document = new W.Document();
        AddStyles(mainPart);
        var (headerId, footerId) = AddHeaderAndFooter(mainPart, date);
        var body = mainPart.Document.AppendChild(new W.Body());

        AddParagraph(body, "FLOATMATE 工作日报", "ReportTitle");
        AddParagraph(body, $"{date:yyyy年M月d日} · 本地工作记录", "Subtitle");

        var completed = ordered.Count(goal => goal.IsCompleted);
        var focusMinutes = (int)Math.Round(ordered.Sum(goal => goal.FocusSeconds) / 60D);
        AddMetadataLine(body, "任务", ordered.Count == 0 ? "暂无任务" : $"{ordered.Count} 项");
        AddMetadataLine(body, "完成", ordered.Count == 0 ? "—" : $"{completed}/{ordered.Count}");
        AddMetadataLine(body, "专注", focusMinutes == 0 ? "暂无专注记录" : $"{focusMinutes} 分钟");

        AddParagraph(body, "今日任务", "Heading1");
        if (ordered.Count == 0)
        {
            AddParagraph(body, "今天还没有工作任务。", "Muted");
        }
        else
        {
            body.Append(CreateTaskTable(ordered));
            AddParagraph(body, "详细工作内容", "Heading1");
            foreach (var goal in ordered)
            {
                AddParagraph(body, goal.Title, "Heading2");
                var meta = $"{goal.StatusLabel} · 进度 {goal.Progress}% · 预计 {goal.EstimateMinutes} 分钟 · 专注 {Math.Round(goal.FocusSeconds / 60D):0} 分钟";
                AddParagraph(body, meta, "Muted");
                var lines = goal.Details.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
                    .Select(line => line.Trim()).Where(line => line.Length > 0).ToList();
                if (lines.Count == 0)
                {
                    AddParagraph(body, "暂无详细工作内容。", "Muted");
                }
                else
                {
                    foreach (var line in lines) AddParagraph(body, line, "Detail");
                }
            }
        }

        body.Append(new W.SectionProperties(
            new W.HeaderReference { Type = W.HeaderFooterValues.Default, Id = headerId },
            new W.FooterReference { Type = W.HeaderFooterValues.Default, Id = footerId },
            new W.PageSize { Width = 12240U, Height = 15840U },
            new W.PageMargin { Top = 1440, Right = 1440U, Bottom = 1440, Left = 1440U, Header = 708U, Footer = 708U, Gutter = 0U }));
        mainPart.Document.Save();
    }

    private static void AddStyles(MainDocumentPart mainPart)
    {
        var stylesPart = mainPart.AddNewPart<StyleDefinitionsPart>();
        stylesPart.Styles = new W.Styles(
            ParagraphStyle("Normal", "正文", "22", Ink, "0", "120", "264", false),
            ParagraphStyle("ReportTitle", "报告标题", "48", Ink, "0", "80", "240", true),
            ParagraphStyle("Subtitle", "副标题", "24", Muted, "0", "240", "264", false),
            ParagraphStyle("Heading1", "一级标题", "32", Accent, "320", "160", "240", true),
            ParagraphStyle("Heading2", "二级标题", "26", Accent, "240", "120", "240", true),
            ParagraphStyle("Muted", "辅助信息", "20", Muted, "0", "80", "240", false),
            ParagraphStyle("Detail", "详细内容", "22", Ink, "0", "100", "264", false, "360"),
            ParagraphStyle("TableText", "表格正文", "19", Ink, "0", "0", "240", false),
            ParagraphStyle("TableHeader", "表格标题", "19", Ink, "0", "0", "240", true));
        stylesPart.Styles.Save();
    }

    private static W.Style ParagraphStyle(string id, string name, string size, string color, string before, string after, string line, bool bold, string? left = null)
    {
        var paragraphProperties = new W.StyleParagraphProperties(
            new W.SpacingBetweenLines { Before = before, After = after, Line = line, LineRule = W.LineSpacingRuleValues.Auto });
        if (left is not null) paragraphProperties.Append(new W.Indentation { Left = left });
        if (id is "ReportTitle" or "Heading1" or "Heading2") paragraphProperties.Append(new W.KeepNext());
        var runProperties = new W.StyleRunProperties(
            new W.RunFonts { Ascii = BodyFont, HighAnsi = BodyFont, EastAsia = BodyFont, ComplexScript = BodyFont },
            new W.Color { Val = color }, new W.FontSize { Val = size }, new W.FontSizeComplexScript { Val = size });
        if (bold) runProperties.Append(new W.Bold());
        var style = new W.Style { Type = W.StyleValues.Paragraph, StyleId = id, CustomStyle = true };
        if (id == "Normal") style.Default = true;
        style.Append(new W.StyleName { Val = name }, paragraphProperties, runProperties);
        return style;
    }

    private static (string HeaderId, string FooterId) AddHeaderAndFooter(MainDocumentPart mainPart, DateOnly date)
    {
        var headerPart = mainPart.AddNewPart<HeaderPart>();
        headerPart.Header = new W.Header(CreateParagraph("FLOATMATE / 工作日报", "Muted"));
        headerPart.Header.Save();

        var footerPart = mainPart.AddNewPart<FooterPart>();
        var footerParagraph = CreateParagraph(string.Empty, "Muted");
        footerParagraph.ParagraphProperties ??= new W.ParagraphProperties();
        footerParagraph.ParagraphProperties.Append(new W.Justification { Val = W.JustificationValues.Right });
        footerParagraph.Append(CreateRun($"{date:yyyy-MM-dd} · 第 ", false));
        footerParagraph.Append(new W.SimpleField(new W.Run(new W.Text("1"))) { Instruction = "PAGE" });
        footerParagraph.Append(CreateRun(" 页", false));
        footerPart.Footer = new W.Footer(footerParagraph);
        footerPart.Footer.Save();
        return (mainPart.GetIdOfPart(headerPart), mainPart.GetIdOfPart(footerPart));
    }

    private static void AddMetadataLine(W.Body body, string label, string value)
    {
        var paragraph = CreateParagraph(string.Empty, "Normal");
        paragraph.ParagraphProperties ??= new W.ParagraphProperties();
        paragraph.ParagraphProperties.SpacingBetweenLines = new W.SpacingBetweenLines { After = "40", Line = "240", LineRule = W.LineSpacingRuleValues.Auto };
        paragraph.Append(CreateRun($"{label}：", true), CreateRun(value, false));
        body.Append(paragraph);
    }

    private static void AddParagraph(W.Body body, string text, string styleId) => body.Append(CreateParagraph(text, styleId));

    private static W.Paragraph CreateParagraph(string text, string styleId)
    {
        var paragraph = new W.Paragraph(new W.ParagraphProperties(new W.ParagraphStyleId { Val = styleId }));
        if (!string.IsNullOrEmpty(text)) paragraph.Append(CreateRun(text, false));
        return paragraph;
    }

    private static W.Run CreateRun(string text, bool bold)
    {
        var properties = new W.RunProperties(
            new W.RunFonts { Ascii = BodyFont, HighAnsi = BodyFont, EastAsia = BodyFont, ComplexScript = BodyFont });
        if (bold) properties.Append(new W.Bold());
        return new W.Run(properties, new W.Text(text) { Space = SpaceProcessingModeValues.Preserve });
    }

    private static W.Table CreateTaskTable(IReadOnlyList<GoalItem> goals)
    {
        var table = new W.Table();
        table.Append(new W.TableProperties(
            new W.TableWidth { Type = W.TableWidthUnitValues.Dxa, Width = "9360" },
            new W.TableIndentation { Type = W.TableWidthUnitValues.Dxa, Width = 120 },
            new W.TableLayout { Type = W.TableLayoutValues.Fixed },
            new W.TableCellMarginDefault(
                new W.TopMargin { Type = W.TableWidthUnitValues.Dxa, Width = "80" },
                new W.TableCellLeftMargin { Type = W.TableWidthValues.Dxa, Width = 120 },
                new W.BottomMargin { Type = W.TableWidthUnitValues.Dxa, Width = "80" },
                new W.TableCellRightMargin { Type = W.TableWidthValues.Dxa, Width = 120 }),
            new W.TableBorders(
                BorderElement<W.TopBorder>(), BorderElement<W.LeftBorder>(), BorderElement<W.BottomBorder>(),
                BorderElement<W.RightBorder>(), BorderElement<W.InsideHorizontalBorder>(), BorderElement<W.InsideVerticalBorder>())));
        var grid = new W.TableGrid();
        foreach (var width in TaskTableWidths) grid.Append(new W.GridColumn { Width = width.ToString() });
        table.Append(grid);
        table.Append(CreateTaskRow(["序号", "任务名称", "状态", "进度", "预计时长", "专注时长"], true));
        for (var index = 0; index < goals.Count; index++)
        {
            var goal = goals[index];
            table.Append(CreateTaskRow([
                (index + 1).ToString(), goal.Title, goal.StatusLabel, $"{goal.Progress}%",
                $"{goal.EstimateMinutes} 分钟", $"{Math.Round(goal.FocusSeconds / 60D):0} 分钟"
            ], false));
        }
        return table;
    }

    private static W.TableRow CreateTaskRow(IReadOnlyList<string> values, bool header)
    {
        var row = new W.TableRow();
        var properties = new W.TableRowProperties(new W.CantSplit());
        if (header) properties.Append(new W.TableHeader());
        row.Append(properties);
        for (var index = 0; index < values.Count; index++)
        {
            var cellProperties = new W.TableCellProperties(
                new W.TableCellWidth { Type = W.TableWidthUnitValues.Dxa, Width = TaskTableWidths[index].ToString() },
                new W.TableCellVerticalAlignment { Val = W.TableVerticalAlignmentValues.Center });
            if (header) cellProperties.Append(new W.Shading { Fill = SoftFill, Val = W.ShadingPatternValues.Clear });
            var paragraph = CreateParagraph(values[index], header ? "TableHeader" : "TableText");
            if (index != 1)
            {
                paragraph.ParagraphProperties ??= new W.ParagraphProperties();
                paragraph.ParagraphProperties.Append(new W.Justification { Val = W.JustificationValues.Center });
            }
            row.Append(new W.TableCell(cellProperties, paragraph));
        }
        return row;
    }

    private static T BorderElement<T>() where T : W.BorderType, new() => new()
    {
        Val = W.BorderValues.Single,
        Color = Border,
        Size = 4U,
        Space = 0U
    };
}
