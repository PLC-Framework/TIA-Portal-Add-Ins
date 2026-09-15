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

        /// <summary>The Report sheet's columns, in order. Named once, for the writer and the reader alike.</summary>
        public static readonly IReadOnlyList<string> ReportColumns = new[]
        {
            "Result", "Level", "PLC", "Software unit", "Object", "Kind", "Name", "Path",
            "Matched", "Suggestions", "Note"
        };

        /// <summary>
        /// The columns a workbook must have to be read as a report at all.
        ///
        /// **The three that report format 2 added are not among them**, so a workbook exported
        /// by an earlier version still imports, with those cells empty. Refusing it would
        /// throw away reports somebody kept, over columns the rows do not need to be read.
        /// </summary>
        private static readonly IReadOnlyList<string> RequiredReportColumns = new[]
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

        // The Info sheet's keys that are read back. Spelled once, because the writer and the
        // reader disagreeing on one would lose that fact on import without a word.
        private const string InfoProject = "Project";
        private const string InfoProjectDirectory = "Project directory";
        private const string InfoScope = "Scope";
        private const string InfoChecked = "Checked (UTC)";
        private const string InfoExported = "Exported (UTC)";
        private const string InfoFramework = "Framework";
        private const string InfoFormat = "Report format";

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

        // ------------------------------------------------------------------ reading

        /// <summary>
        /// Reads a workbook this class wrote back into a report. Null when it is not one, with the
        /// reason in <paramref name="problem"/> - never an exception, since the caller is a window
        /// that must stay usable.
        ///
        /// **It reads what Excel leaves behind, not only what was written.** Opened and saved in
        /// Excel, a workbook has its text moved into the shared string table, its columns possibly
        /// rearranged and its rows sorted. So columns are found by their header, not their
        /// position; every kind of text cell is read; and a sorted sheet simply gives rows in
        /// that order. A column that is missing is refused by name - quietly reading the rest
        /// would present a report with a hole in it as whole.
        /// </summary>
        public static StyleReport Read(string path, out string problem)
        {
            problem = null;

            try
            {
                // Shared for reading and writing: Excel keeps the file open while it shows it, and
                // importing a report somebody still has open is an ordinary thing to do.
                using (System.IO.FileStream stream = new System.IO.FileStream(
                           path, System.IO.FileMode.Open, System.IO.FileAccess.Read, System.IO.FileShare.ReadWrite))
                using (SpreadsheetDocument document = SpreadsheetDocument.Open(stream, false))
                {
                    WorkbookPart workbook = document.WorkbookPart;
                    if (workbook?.Workbook?.Sheets == null)
                    {
                        problem = "The file holds no sheets.";
                        return null;
                    }

                    string[] shared = SharedStrings(workbook);

                    List<string[]> reportRows = ReadSheet(workbook, ReportSheet, shared);
                    if (reportRows == null)
                    {
                        problem = "This is not a coding-style report: it has no '" + ReportSheet + "' sheet.";
                        return null;
                    }

                    Dictionary<string, string> info = Facts(ReadSheet(workbook, InfoSheet, shared));

                    if (info.TryGetValue(InfoFormat, out string formatText) &&
                        int.TryParse(formatText, NumberStyles.Integer, CultureInfo.InvariantCulture, out int format) &&
                        format > StyleReport.CurrentFormat)
                    {
                        problem = "This report was written by a newer version of the framework (format " + format +
                                  "); this window reads format " + StyleReport.CurrentFormat + ".";
                        return null;
                    }

                    List<ReportRow> rows = Rows(reportRows, out problem);
                    if (rows == null) return null;

                    List<ReportRule> rules = Rules(ReadSheet(workbook, RulesSheet, shared), out problem);
                    if (rules == null) return null;

                    return new StyleReport
                    {
                        Format = StyleReport.CurrentFormat,
                        GeneratedAtUtc = Fact(info, InfoChecked),
                        Framework = Fact(info, InfoFramework),
                        Project = Fact(info, InfoProject),
                        ProjectDirectory = Fact(info, InfoProjectDirectory),
                        Scope = Fact(info, InfoScope),
                        Rules = rules,
                        Rows = rows
                    };
                }
            }
            catch (Exception exception)
            {
                problem = "The workbook could not be read: " + exception.Message;
                return null;
            }
        }

        private static List<ReportRow> Rows(List<string[]> sheet, out string problem)
        {
            problem = null;

            Dictionary<string, int> columns = Header(sheet, RequiredReportColumns, ReportSheet, out problem);
            if (columns == null) return null;

            List<ReportRow> rows = new List<ReportRow>();

            foreach (string[] cells in sheet.Skip(1))
            {
                if (cells.All(string.IsNullOrWhiteSpace)) continue;

                string matched = Value(cells, columns, "Matched");
                string suggestions = Value(cells, columns, "Suggestions");

                rows.Add(new ReportRow
                {
                    Outcome = Outcomes.Parse(Value(cells, columns, "Result")),
                    Scope = string.Equals(Value(cells, columns, "Level"), "Member", StringComparison.OrdinalIgnoreCase)
                        ? RowScope.Member.ToString()
                        : RowScope.Object.ToString(),
                    Plc = Value(cells, columns, "PLC"),
                    Unit = Value(cells, columns, "Software unit"),
                    Owner = Value(cells, columns, "Object"),
                    Kind = Value(cells, columns, "Kind"),
                    Name = Value(cells, columns, "Name"),
                    Path = Value(cells, columns, "Path"),
                    Matched = Ids(matched),
                    Suggestions = Ids(suggestions),
                    Note = NullIfEmpty(Value(cells, columns, "Note"))
                });
            }

            return rows;
        }

        /// <summary>
        /// The rules, or an empty list when the sheet is gone. A report without them still reads -
        /// its tooltips say the rules are not described - where a report without rows would not.
        /// </summary>
        private static List<ReportRule> Rules(List<string[]> sheet, out string problem)
        {
            problem = null;
            List<ReportRule> rules = new List<ReportRule>();
            if (sheet == null || sheet.Count == 0) return rules;

            Dictionary<string, int> columns = Header(sheet, RulesColumns, RulesSheet, out problem);
            if (columns == null) return null;

            foreach (string[] cells in sheet.Skip(1))
            {
                string id = Value(cells, columns, "Id");
                if (string.IsNullOrWhiteSpace(id)) continue;

                string description = Value(cells, columns, "Description");

                rules.Add(new ReportRule
                {
                    Id = id,
                    Catalogue = Value(cells, columns, "Catalogue"),
                    Regex = Value(cells, columns, "Pattern"),
                    Descriptions = string.IsNullOrEmpty(description)
                        ? new List<string>()
                        : description.Split('\n').ToList()
                });
            }

            return rules;
        }

        /// <summary>Where each expected column is, by its header text, or null naming the ones missing.</summary>
        private static Dictionary<string, int> Header(List<string[]> sheet, IReadOnlyList<string> expected, string name, out string problem)
        {
            problem = null;

            string[] header = sheet.FirstOrDefault() ?? new string[0];
            Dictionary<string, int> columns = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            for (int i = 0; i < header.Length; i++)
            {
                string text = (header[i] ?? string.Empty).Trim();
                if (text.Length > 0 && !columns.ContainsKey(text)) columns.Add(text, i);
            }

            List<string> missing = expected.Where(column => !columns.ContainsKey(column)).ToList();
            if (missing.Count == 0) return columns;

            problem = "This is not a coding-style report the window can read: the '" + name +
                      "' sheet has no " + string.Join(", ", missing.Select(m => "'" + m + "'")) + " column.";
            return null;
        }

        /// <summary>The sheet's rows as text, each cell at its column's index. Null when there is no such sheet.</summary>
        private static List<string[]> ReadSheet(WorkbookPart workbook, string name, string[] shared)
        {
            Sheet sheet = workbook.Workbook.Sheets.Elements<Sheet>()
                .FirstOrDefault(candidate => string.Equals(candidate.Name?.Value, name, StringComparison.OrdinalIgnoreCase));

            if (sheet?.Id?.Value == null) return null;
            if (!(workbook.GetPartById(sheet.Id.Value) is WorksheetPart part)) return null;

            List<string[]> rows = new List<string[]>();
            SheetData data = part.Worksheet?.GetFirstChild<SheetData>();
            if (data == null) return rows;

            foreach (Row row in data.Elements<Row>())
            {
                List<KeyValuePair<int, string>> cells = new List<KeyValuePair<int, string>>();
                int position = 0;

                foreach (Cell cell in row.Elements<Cell>())
                {
                    // By reference when there is one - Excel omits empty cells, so position alone
                    // would shift every value after a gap one column to the left.
                    int index = cell.CellReference?.Value != null ? ColumnIndex(cell.CellReference.Value) : position;
                    cells.Add(new KeyValuePair<int, string>(index, TextOf(cell, shared)));
                    position = index + 1;
                }

                string[] values = new string[cells.Count == 0 ? 0 : cells.Max(cell => cell.Key) + 1];
                foreach (KeyValuePair<int, string> cell in cells) values[cell.Key] = cell.Value;

                rows.Add(values);
            }

            return rows;
        }

        private static string[] SharedStrings(WorkbookPart workbook)
        {
            SharedStringTable table = workbook.SharedStringTablePart?.SharedStringTable;

            // Materialised once: looking each index up in the table would make a large sheet
            // quadratic.
            return table == null
                ? new string[0]
                : table.Elements<SharedStringItem>().Select(item => Clean(item.InnerText)).ToArray();
        }

        private static string TextOf(Cell cell, string[] shared)
        {
            CellValues? type = cell.DataType?.Value;

            if (type == CellValues.InlineString) return Clean(cell.InlineString?.InnerText);

            if (type == CellValues.SharedString)
            {
                return int.TryParse(cell.CellValue?.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int index) &&
                       index >= 0 && index < shared.Length
                    ? shared[index]
                    : null;
            }

            if (type == CellValues.Boolean) return cell.CellValue?.Text == "1" ? "TRUE" : "FALSE";

            // A number, or the cached result of a formula somebody typed in.
            return Clean(cell.CellValue?.Text);
        }

        /// <summary>
        /// Undoes what Excel does to text on saving: a carriage return written as _x000D_, and
        /// line breaks as \r\n where this class wrote \n.
        /// </summary>
        private static string Clean(string text) =>
            text?.Replace("_x000D_", string.Empty).Replace("\r\n", "\n").Replace("\r", "\n");

        private static Dictionary<string, string> Facts(List<string[]> sheet)
        {
            Dictionary<string, string> facts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (sheet == null) return facts;

            foreach (string[] cells in sheet)
            {
                string key = ValueAt(cells, 0);
                if (!string.IsNullOrWhiteSpace(key) && !facts.ContainsKey(key.Trim())) facts.Add(key.Trim(), ValueAt(cells, 1));
            }

            return facts;
        }

        private static string Fact(Dictionary<string, string> facts, string key) =>
            facts.TryGetValue(key, out string value) ? NullIfEmpty(value) : null;

        private static string ValueAt(string[] cells, int index) =>
            index >= 0 && index < cells.Length ? cells[index] ?? string.Empty : string.Empty;

        /// <summary>
        /// One cell by its column's header, and empty when the sheet has no such column - which
        /// is how a workbook written before a column existed still reads.
        /// </summary>
        private static string Value(string[] cells, Dictionary<string, int> columns, string column) =>
            columns.TryGetValue(column, out int index) ? ValueAt(cells, index) : string.Empty;

        private static List<string> Ids(string cell) =>
            (cell ?? string.Empty)
                .Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(id => id.Trim())
                .Where(id => id.Length > 0)
                .ToList();

        private static string NullIfEmpty(string text) => string.IsNullOrEmpty(text) ? null : text;

        private static int ColumnIndex(string reference)
        {
            int index = 0;

            foreach (char character in reference)
            {
                if (character < 'A' || character > 'Z') break;
                index = index * 26 + (character - 'A' + 1);
            }

            return index - 1;
        }

        // ------------------------------------------------------------------ writing

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
                    ColumnWidth(1, 16), ColumnWidth(2, 10), ColumnWidth(3, 16), ColumnWidth(4, 16),
                    ColumnWidth(5, 28), ColumnWidth(6, 16), ColumnWidth(7, 32), ColumnWidth(8, 40),
                    ColumnWidth(9, 34), ColumnWidth(10, 40), ColumnWidth(11, 60)));

                writer.WriteStartElement(new SheetData());

                WriteHeader(writer, line++, ReportColumns);

                foreach (ReportRow row in report.Rows)
                {
                    if (row == null) continue;

                    // Coloured as the window colours it, so a printed or shared sheet reads the
                    // same; members indented under their object instead of by padded names, so a
                    // cell holds the name exactly and a filter on it still matches.
                    //
                    // **Only the name is indented**, not the whole row: the columns that say where
                    // the row comes from line up down the sheet, and a member is shown under its
                    // object by the one cell that is about the object's contents.
                    uint style = Outcomes.IsFailure(row) ? StyleFailure
                               : Outcomes.IsUnjudged(row) ? StyleUnjudged : StylePlain;

                    uint nameStyle = !row.IsMember ? style
                                   : Outcomes.IsFailure(row) ? StyleMemberFailure
                                   : Outcomes.IsUnjudged(row) ? StyleMemberUnjudged : StyleMemberPlain;

                    writer.WriteStartElement(new Row { RowIndex = line });

                    WriteText(writer, "A", line, Outcomes.Label(row), style);
                    WriteText(writer, "B", line, row.IsMember ? "Member" : "Object", style);
                    WriteText(writer, "C", line, row.Plc, style);
                    WriteText(writer, "D", line, row.Unit, style);
                    WriteText(writer, "E", line, row.Owner, style);
                    WriteText(writer, "F", line, row.Kind, style);
                    WriteText(writer, "G", line, row.Name, nameStyle);
                    WriteText(writer, "H", line, row.Path, style);
                    WriteText(writer, "I", line, string.Join(ListSeparator, row.Matched), style);
                    // Suggestions only where nothing matched, as the window shows them. The checker
                    // lists every rule a name missed, so a passed FB would otherwise carry ten
                    // "suggestions" for names it has no business being.
                    WriteText(writer, "J", line, row.Matched.Count == 0 ? string.Join(ListSeparator, row.Suggestions) : null, style);
                    WriteText(writer, "K", line, row.Note, style);

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
                Fact(InfoProject, report.Project),
                Fact(InfoProjectDirectory, report.ProjectDirectory),
                Fact(InfoScope, report.Scope),
                Fact(InfoChecked, report.GeneratedAtUtc),
                Fact(InfoExported, exportedUtc.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture)),
                Fact(InfoFramework, report.Framework),
                Fact(InfoFormat, report.Format),
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
