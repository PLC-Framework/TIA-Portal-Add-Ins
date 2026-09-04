using System;
using System.Collections.Generic;
using System.Globalization;

using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;

namespace Satellite.DataBlockSnapshot.Export
{
    /// <summary>
    /// Writes a capture as a workbook: the values on one sheet, the provenance on
    /// another.
    ///
    /// Cells are typed, which is the reason to offer this format at all. A CSV hands
    /// Excel text and lets it guess, and Excel guesses badly - turning 1.10 into a date
    /// and dropping the leading zero off an identifier. Here a number arrives as a
    /// number and a bool as a bool, with nothing left to interpret.
    /// </summary>
    public static class XlsxExporter
    {
        public static void Write(Snapshot snapshot, string path)
        {
            if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));

            using (SpreadsheetDocument document =
                   SpreadsheetDocument.Create(path, SpreadsheetDocumentType.Workbook))
            {
                WorkbookPart workbook = document.AddWorkbookPart();
                WorksheetPart values = workbook.AddNewPart<WorksheetPart>();
                WorksheetPart info = workbook.AddNewPart<WorksheetPart>();

                WriteValues(values, snapshot);
                WriteInfo(info, snapshot);

                workbook.Workbook = new Workbook(
                    new Sheets(
                        new Sheet
                        {
                            Id = workbook.GetIdOfPart(values),
                            SheetId = 1U,
                            Name = "Snapshot"
                        },
                        new Sheet
                        {
                            Id = workbook.GetIdOfPart(info),
                            SheetId = 2U,
                            Name = "Info"
                        }));
            }
        }

        // Written with OpenXmlWriter rather than by building the document in memory: a
        // data block can hold tens of thousands of variables, and the DOM approach keeps
        // every one of them alive at once.
        private static void WriteValues(WorksheetPart part, Snapshot snapshot)
        {
            using (OpenXmlWriter writer = OpenXmlWriter.Create(part))
            {
                writer.WriteStartElement(new Worksheet());
                writer.WriteStartElement(new SheetData());

                uint line = 1;
                WriteRow(writer, line++, new[] { "Variable", "DataType", "Value", "Error", "ReadOnly" });

                foreach (SnapshotRow row in snapshot.Rows)
                {
                    writer.WriteStartElement(new Row { RowIndex = line });

                    WriteText(writer, "A", line, row.Variable);
                    WriteText(writer, "B", line, row.DataType);
                    WriteValue(writer, "C", line, row.Value);
                    WriteText(writer, "D", line, row.Error);
                    WriteBool(writer, "E", line, row.ReadOnly);

                    writer.WriteEndElement();
                    line++;
                }

                writer.WriteEndElement();
                writer.WriteEndElement();
                writer.Close();
            }
        }

        private static void WriteInfo(WorksheetPart part, Snapshot snapshot)
        {
            List<KeyValuePair<string, string>> facts = new List<KeyValuePair<string, string>>
            {
                new KeyValuePair<string, string>("PLC", snapshot.PlcAddress),
                new KeyValuePair<string, string>("Data block", snapshot.DataBlock),
                new KeyValuePair<string, string>("Started", ValueText.Timestamp(snapshot.StartedAt)),
                new KeyValuePair<string, string>("Completed", ValueText.Timestamp(snapshot.CompletedAt)),
                new KeyValuePair<string, string>("Duration (s)",
                    snapshot.Duration.TotalSeconds.ToString("F1", CultureInfo.InvariantCulture)),
                new KeyValuePair<string, string>("Variables",
                    snapshot.Rows.Count.ToString(CultureInfo.InvariantCulture)),
                new KeyValuePair<string, string>("Failed",
                    snapshot.FailedCount.ToString(CultureInfo.InvariantCulture)),
                new KeyValuePair<string, string>("Truncated", snapshot.Truncated ? "true" : "false")
            };

            if (snapshot.Truncated)
                facts.Add(new KeyValuePair<string, string>(
                    "WARNING", "The browse stopped at a limit. Variables are missing from this file."));

            using (OpenXmlWriter writer = OpenXmlWriter.Create(part))
            {
                writer.WriteStartElement(new Worksheet());
                writer.WriteStartElement(new SheetData());

                uint line = 1;
                foreach (KeyValuePair<string, string> fact in facts)
                    WriteRow(writer, line++, new[] { fact.Key, fact.Value });

                writer.WriteEndElement();
                writer.WriteEndElement();
                writer.Close();
            }
        }

        private static void WriteRow(OpenXmlWriter writer, uint line, string[] cells)
        {
            writer.WriteStartElement(new Row { RowIndex = line });

            for (int i = 0; i < cells.Length; i++)
                WriteText(writer, Column(i), line, cells[i]);

            writer.WriteEndElement();
        }

        /// <summary>
        /// A text cell, omitted when there is nothing to say. Used for the columns where
        /// an absent cell is unambiguous: no error means no error.
        /// </summary>
        private static void WriteText(OpenXmlWriter writer, string column, uint line, string text)
        {
            if (string.IsNullOrEmpty(text)) return;

            WriteInlineString(writer, column, line, text);
        }

        /// <summary>
        /// A text cell that is written even when empty.
        ///
        /// This is the difference between "this setting is an empty string" and "this
        /// value could not be read", which an omitted cell renders identically. In a
        /// capture meant to record a configuration exactly, those two must not look
        /// the same.
        /// </summary>
        private static void WriteInlineString(OpenXmlWriter writer, string column, uint line, string text)
        {
            writer.WriteStartElement(new Cell
            {
                CellReference = column + line.ToString(CultureInfo.InvariantCulture),
                DataType = CellValues.InlineString
            });

            // Inline rather than through the shared string table: the table pays off for
            // repeated text, and a column of distinct variable names has none.
            //
            // Space preserved, so a setting whose value ends in a blank keeps it. XML
            // collapses whitespace by default, which would silently edit the data.
            writer.WriteElement(new InlineString(
                new Text(text) { Space = SpaceProcessingModeValues.Preserve }));

            writer.WriteEndElement();
        }

        private static void WriteBool(OpenXmlWriter writer, string column, uint line, bool value)
        {
            writer.WriteStartElement(new Cell
            {
                CellReference = column + line.ToString(CultureInfo.InvariantCulture),
                DataType = CellValues.Boolean
            });

            writer.WriteElement(new CellValue(value ? "1" : "0"));
            writer.WriteEndElement();
        }

        private static void WriteValue(OpenXmlWriter writer, string column, uint line, object value)
        {
            if (value == null) return;

            if (value is bool flag)
            {
                WriteBool(writer, column, line, flag);
                return;
            }

            if (value is string text)
            {
                WriteInlineString(writer, column, line, text);
                return;
            }

            // Everything else out of the web API is a number: long for the integer
            // families and time, double for real.
            writer.WriteStartElement(new Cell
            {
                CellReference = column + line.ToString(CultureInfo.InvariantCulture),
                DataType = CellValues.Number
            });

            writer.WriteElement(new CellValue(ValueText.Render(value)));
            writer.WriteEndElement();
        }

        private static string Column(int index)
        {
            string name = string.Empty;

            for (int i = index; i >= 0; i = i / 26 - 1)
                name = (char)('A' + i % 26) + name;

            return name;
        }
    }
}
