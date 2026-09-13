using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;

using Core.Checks;

using Satellite.CodingStyleReport.Report;

namespace Satellite.CodingStyleReport.Export
{
    /// <summary>
    /// A coding-style report as a workbook - the framework's one export format, the same as the
    /// data block snapshot writes.
    ///
    /// **Three sheets, and together they are the whole report.** Report holds every row, Rules
    /// the rules those rows name, Info the provenance and the counts. Nothing the window shows
    /// is left behind, which is what lets a workbook be opened again later as a report rather
    /// than only read as a spreadsheet.
    ///
    /// **Every row is exported, whatever the window's filters show.** A file of the failures
    /// alone would look like a clean report to whoever opens it next; the spreadsheet's own
    /// autofilter is there for narrowing.
    /// </summary>
    public static class ReportWorkbook
    {
        public const string ReportSheet = "Report";
        public const string RulesSheet = "Rules";
        public const string InfoSheet = "Info";

        /// <summary>The Report sheet's columns, in order. Named once, for the reader that will come.</summary>
        public static readonly IReadOnlyList<string> ReportColumns = new[]
        {
            "Result", "Level", "Kind", "Name", "Path", "Matched", "Suggestions", "Note"
        };

        public static readonly IReadOnlyList<string> RulesColumns = new[]
        {
            "Id", "Catalogue", "Pattern", "Description"
        };

        /// <summary>
        /// How a list of rule ids is written into one cell. A comma reads naturally in a
        /// spreadsheet, and a rule id is a key that does not carry one.
        /// </summary>
        public const string ListSeparator = ", ";

        // Cell formats, by index into the stylesheet below.
        private const uint StylePlain = 0;
        private const uint StyleHeader = 1;
        private const uint StyleFailure = 2;
        private const uint StyleUnjudged = 3;
        private const uint StyleMemberPlain = 4;
        private const uint StyleMemberFailure = 5;
        private const uint StyleMemberUnjudged = 6;
        private const uint StyleWrapped = 7;
        private const uint StyleKey = 8;

        public static void Write(StyleReport report, string path, DateTime exportedUtc)
        {
            if (report == null) throw new ArgumentNullException(nameof(report));

            using (SpreadsheetDocument document = SpreadsheetDocument.Create(path, SpreadsheetDocumentType.Workbook))
            {
                WorkbookPart workbook = document.AddWorkbookPart();

                WorkbookStylesPart styles = workbook.AddNewPart<WorkbookStylesPart>();
                styles.Stylesheet = BuildStylesheet();

                WorksheetPart rows = workbook.AddNewPart<WorksheetPart>();
                WorksheetPart rules = workbook.AddNewPart<WorksheetPart>();
                WorksheetPart info = workbook.AddNewPart<WorksheetPart>();

                uint lastRow = WriteReport(rows, report);
                WriteRules(rules, report);
                WriteInfo(info, report, exportedUtc);

                workbook.Workbook = new Workbook(
                    new Sheets(
                        new Sheet { Id = workbook.GetIdOfPart(rows), SheetId = 1U, Name = ReportSheet },
                        new Sheet { Id = workbook.GetIdOfPart(rules), SheetId = 2U, Name = RulesSheet },
                        new Sheet { Id = workbook.GetIdOfPart(info), SheetId = 3U, Name = InfoSheet }),
                    // The name Excel itself keeps for an autofilter's range. Without it the filter
                    // still works, but Excel treats the range as unnamed and rewrites it on save.
                    new DefinedNames(
                        new DefinedName("'" + ReportSheet + "'!$A$1:$" + ColumnName(ReportColumns.Count - 1) + "$" + lastRow)
                        {
                            Name = "_xlnm._FilterDatabase",
                            LocalSheetId = 0U,
                            Hidden = true
                        }));
            }
        }

        // Written with OpenXmlWriter rather than a document built in memory: a project-wide check
        // can produce tens of thousands of rows, and the DOM keeps every one alive at once.
        private static uint WriteReport(WorksheetPart part, StyleReport report)
        {
            uint line = 1;

            using (OpenXmlWriter writer = OpenXmlWriter.Create(part))
            {
                writer.WriteStartElement(new Worksheet());

                // The header stays in view however far down the reader scrolls.
                writer.WriteElement(new SheetViews(
                    new SheetView(
                        new Pane
                        {
                            VerticalSplit = 1D,
                            TopLeftCell = "A2",
                            ActivePane = PaneValues.BottomLeft,
                            State = PaneStateValues.Frozen
                        },
                        new Selection { Pane = PaneValues.BottomLeft })
                    { WorkbookViewId = 0U }));

                writer.WriteElement(new Columns(
                    ColumnWidth(1, 16), ColumnWidth(2, 10), ColumnWidth(3, 16), ColumnWidth(4, 32),
                    ColumnWidth(5, 44), ColumnWidth(6, 34), ColumnWidth(7, 40), ColumnWidth(8, 60)));

                writer.WriteStartElement(new SheetData());

                WriteHeader(writer, line++, ReportColumns);

                foreach (ReportRow row in report.Rows)
                {
                    if (row == null) continue;

                    // Coloured as the window colours it, so a printed or shared sheet reads the
                    // same; members indented under their object instead of by padded names, so a
                    // cell holds the name exactly and a filter on it still matches.
                    uint style = row.IsMember
                        ? Outcomes.IsFailure(row) ? StyleMemberFailure : Outcomes.IsUnjudged(row) ? StyleMemberUnjudged : StyleMemberPlain
                        : Outcomes.IsFailure(row) ? StyleFailure : Outcomes.IsUnjudged(row) ? StyleUnjudged : StylePlain;

                    writer.WriteStartElement(new Row { RowIndex = line });

                    WriteText(writer, "A", line, Outcomes.Label(row), style);
                    WriteText(writer, "B", line, row.IsMember ? "Member" : "Object", style);
                    WriteText(writer, "C", line, row.Kind, style);
                    WriteText(writer, "D", line, row.Name, style);
                    WriteText(writer, "E", line, row.Path, style);
                    WriteText(writer, "F", line, string.Join(ListSeparator, row.Matched), style);
                    // Suggestions only where nothing matched, as the window shows them. The checker
                    // lists every rule a name missed, so a passed FB would otherwise carry ten
                    // "suggestions" for names it has no business being.
                    WriteText(writer, "G", line, row.Matched.Count == 0 ? string.Join(ListSeparator, row.Suggestions) : null, style);
                    WriteText(writer, "H", line, row.Note, style);

                    writer.WriteEndElement();
                    line++;
                }

                writer.WriteEndElement();

                writer.WriteElement(new AutoFilter { Reference = "A1:" + ColumnName(ReportColumns.Count - 1) + (line - 1) });

                writer.WriteEndElement();
                writer.Close();
            }

            return line - 1;
        }

        private static void WriteRules(WorksheetPart part, StyleReport report)
        {
            using (OpenXmlWriter writer = OpenXmlWriter.Create(part))
            {
                writer.WriteStartElement(new Worksheet());
                writer.WriteElement(new Columns(ColumnWidth(1, 30), ColumnWidth(2, 12), ColumnWidth(3, 60), ColumnWidth(4, 90)));
                writer.WriteStartElement(new SheetData());

                uint line = 1;
                WriteHeader(writer, line++, RulesColumns);

                foreach (ReportRule rule in report.Rules)
                {
                    if (rule == null) continue;

                    writer.WriteStartElement(new Row { RowIndex = line });

                    WriteText(writer, "A", line, rule.Id, StylePlain);
                    WriteText(writer, "B", line, rule.Catalogue, StylePlain);
                    WriteText(writer, "C", line, rule.Regex, StylePlain);
                    // One line per description line, in one cell: a rule's description is read
                    // as a whole, and splitting it across rows would break the sheet's filter.
                    WriteText(writer, "D", line, string.Join("\n", rule.Descriptions ?? new List<string>()), StyleWrapped);

                    writer.WriteEndElement();
                    line++;
                }

                writer.WriteEndElement();
                writer.WriteEndElement();
                writer.Close();
            }
        }

        private static void WriteInfo(WorksheetPart part, StyleReport report, DateTime exportedUtc)
        {
            List<ReportRow> rows = report.Rows.Where(row => row != null).ToList();

            List<KeyValuePair<string, object>> facts = new List<KeyValuePair<string, object>>
            {
                Fact("Project", report.Project),
                Fact("Project directory", report.ProjectDirectory),
                Fact("Scope", report.Scope),
                Fact("Checked (UTC)", report.GeneratedAtUtc),
                Fact("Exported (UTC)", exportedUtc.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture)),
                Fact("Framework", report.Framework),
                Fact("Report format", report.Format),
                Fact("Objects", rows.Count(row => !row.IsMember)),
                Fact("Members", rows.Count(row => row.IsMember)),
                Fact("Passed", rows.Count(row => row.OutcomeValue == CheckOutcome.Passed)),
                Fact("Failed", rows.Count(row => row.OutcomeValue == CheckOutcome.Failed)),
                Fact("Not configured", rows.Count(row => row.OutcomeValue == CheckOutcome.NotConfigured)),
                Fact("Skipped", rows.Count(row => row.OutcomeValue == CheckOutcome.Skipped))
            };

            using (OpenXmlWriter writer = OpenXmlWriter.Create(part))
            {
                writer.WriteStartElement(new Worksheet());
                writer.WriteElement(new Columns(ColumnWidth(1, 20), ColumnWidth(2, 60)));
                writer.WriteStartElement(new SheetData());

                uint line = 1;
                foreach (KeyValuePair<string, object> fact in facts)
                {
                    writer.WriteStartElement(new Row { RowIndex = line });

                    WriteText(writer, "A", line, fact.Key, StyleKey);

                    // Counts and the format as numbers, so they add up in a formula.
                    if (fact.Value is int number) WriteNumber(writer, "B", line, number);
                    else WriteText(writer, "B", line, fact.Value as string, StylePlain);

                    writer.WriteEndElement();
                    line++;
                }

                writer.WriteEndElement();
                writer.WriteEndElement();
                writer.Close();
            }
        }

        private static KeyValuePair<string, object> Fact(string key, object value) =>
            new KeyValuePair<string, object>(key, value);

        private static void WriteHeader(OpenXmlWriter writer, uint line, IReadOnlyList<string> names)
        {
            writer.WriteStartElement(new Row { RowIndex = line });

            for (int i = 0; i < names.Count; i++)
                WriteText(writer, ColumnName(i), line, names[i], StyleHeader);

            writer.WriteEndElement();
        }

        /// <summary>An inline text cell, omitted when there is nothing in it.</summary>
        private static void WriteText(OpenXmlWriter writer, string column, uint line, string text, uint style)
        {
            if (string.IsNullOrEmpty(text)) return;

            writer.WriteStartElement(new Cell
            {
                CellReference = column + line.ToString(CultureInfo.InvariantCulture),
                DataType = CellValues.InlineString,
                StyleIndex = style
            });

            // Space preserved: XML collapses whitespace by default, which would edit a name
            // that ends in a blank - exactly the kind of name a naming check exists to catch.
            writer.WriteElement(new InlineString(new Text(text) { Space = SpaceProcessingModeValues.Preserve }));

            writer.WriteEndElement();
        }

        private static void WriteNumber(OpenXmlWriter writer, string column, uint line, int value)
        {
            writer.WriteStartElement(new Cell
            {
                CellReference = column + line.ToString(CultureInfo.InvariantCulture),
                DataType = CellValues.Number
            });

            writer.WriteElement(new CellValue(value.ToString(CultureInfo.InvariantCulture)));
            writer.WriteEndElement();
        }

        private static Column ColumnWidth(uint index, double width) =>
            new Column { Min = index, Max = index, Width = width, CustomWidth = true };

        /// <summary>
        /// The formats the cells above refer to by index. Colours are darker than the window's -
        /// the window paints on a dark surface, a spreadsheet on white, and the same red that
        /// reads well on one is faint on the other.
        /// </summary>
        private static Stylesheet BuildStylesheet()
        {
            return new Stylesheet(
                new Fonts(
                    MakeFont(bold: false, rgb: null),
                    MakeFont(bold: true, rgb: null),
                    MakeFont(bold: false, rgb: "FFC00000"),
                    MakeFont(bold: false, rgb: "FFB36B00"))
                { Count = 4U },
                new Fills(
                    new Fill(new PatternFill { PatternType = PatternValues.None }),
                    new Fill(new PatternFill { PatternType = PatternValues.Gray125 }),
                    new Fill(new PatternFill(
                        new ForegroundColor { Rgb = "FFE7E9EE" },
                        new BackgroundColor { Indexed = 64U })
                    { PatternType = PatternValues.Solid }))
                { Count = 3U },
                new Borders(
                    new Border(new LeftBorder(), new RightBorder(), new TopBorder(), new BottomBorder(), new DiagonalBorder()))
                { Count = 1U },
                new CellStyleFormats(
                    new CellFormat { NumberFormatId = 0U, FontId = 0U, FillId = 0U, BorderId = 0U })
                { Count = 1U },
                new CellFormats(
                    MakeFormat(font: 0, fill: 0),                    // Plain
                    MakeFormat(font: 1, fill: 2),                    // Header
                    MakeFormat(font: 2, fill: 0),                    // Failure
                    MakeFormat(font: 3, fill: 0),                    // Unjudged
                    MakeFormat(font: 0, fill: 0, indent: 1),         // MemberPlain
                    MakeFormat(font: 2, fill: 0, indent: 1),         // MemberFailure
                    MakeFormat(font: 3, fill: 0, indent: 1),         // MemberUnjudged
                    MakeFormat(font: 0, fill: 0, wrap: true),        // Wrapped
                    MakeFormat(font: 1, fill: 0))                    // Key
                { Count = 9U },
                new CellStyles(
                    new CellStyle { Name = "Normal", FormatId = 0U, BuiltinId = 0U })
                { Count = 1U });
        }

        private static Font MakeFont(bool bold, string rgb)
        {
            Font font = new Font();

            // The schema fixes the order of a font's children: bold, then size, colour, name.
            if (bold) font.Append(new Bold());
            font.Append(new FontSize { Val = 11D });
            if (rgb != null) font.Append(new Color { Rgb = rgb });
            font.Append(new FontName { Val = "Calibri" });

            return font;
        }

        private static CellFormat MakeFormat(uint font, uint fill, uint indent = 0, bool wrap = false)
        {
            CellFormat format = new CellFormat
            {
                NumberFormatId = 0U,
                FontId = font,
                FillId = fill,
                BorderId = 0U,
                FormatId = 0U,
                ApplyFont = font != 0,
                ApplyFill = fill != 0
            };

            if (indent > 0 || wrap)
            {
                format.ApplyAlignment = true;
                format.Append(wrap
                    ? new Alignment { WrapText = true, Vertical = VerticalAlignmentValues.Top }
                    : new Alignment { Indent = indent });
            }

            return format;
        }

        private static string ColumnName(int index)
        {
            string name = string.Empty;

            for (int i = index; i >= 0; i = i / 26 - 1)
                name = (char)('A' + i % 26) + name;

            return name;
        }
    }
}
