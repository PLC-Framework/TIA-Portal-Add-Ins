using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Xml.Linq;

namespace Core.Repo.PlcCore
{
    /// <summary>
    /// One of the core's <c>.xlsx</c> tag tables, read back as the objects TIA has to be given.
    ///
    /// **Openness will not take the workbook.** `PlcTagTableComposition.Import` reads whatever it
    /// is handed as SimaticML, and on the VM a workbook came back as *"Invalid XML encountered
    /// while reading Simatic ML file: Data at the root level is invalid. Line 1, position 1."*
    /// TIA Portal imports Excel from its own user interface; Openness does not offer that door. So
    /// the table is built object by object instead — `PlcTagTableComposition.Create(name)`,
    /// `PlcUserConstantComposition.Create(name, dataTypeName, value)` and
    /// `PlcTagComposition.Create(name, dataTypeName, logicalAddress)`, all of which do exist.
    ///
    /// **Read here rather than with `DocumentFormat.OpenXml`.** Writing a real workbook is five
    /// XML parts in a zip whose only true test is whether Excel opens it, which is why the SDK
    /// earned its place for the exports; *reading* a shape this file already knows is one zip
    /// entry and two documents. And `Core` may not take that package at all — it is loaded inside
    /// TIA Portal's process. What makes the reader worth anything is not the parser but the
    /// fifteen real workbooks it is run against.
    ///
    /// **Sheets are found by name and columns by header, never by position.** Both were measured
    /// rather than assumed, and both would have been wrong: `EAdtConstants` puts `Constants`
    /// first while `system` puts `PLC Tags` first, and the sheet parts are not reliably named
    /// `sheet1.xml` either — one workbook in the folder calls its first part `sheet.xml`.
    /// </summary>
    public sealed class ConstantsWorkbook
    {
        /// <summary>The sheet holding the user constants, and the table's own row.</summary>
        public const string ConstantsSheet = "Constants";

        /// <summary>The sheet holding the PLC tags, when the table has any.</summary>
        public const string TagsSheet = "PLC Tags";

        private static readonly PlcCoreConstant[] NoConstants = new PlcCoreConstant[0];
        private static readonly PlcCoreTag[] NoTags = new PlcCoreTag[0];

        private ConstantsWorkbook(
            string name,
            string folder,
            string title,
            IReadOnlyList<PlcCoreConstant> constants,
            IReadOnlyList<PlcCoreTag> tags,
            string problem)
        {
            Name = name;
            Folder = folder;
            Title = title;
            Constants = constants;
            Tags = tags;
            Problem = problem;
        }

        /// <summary>The tag table's own name, off the first row of <c>Constants</c>.</summary>
        public string Name { get; }

        /// <summary>
        /// The family, as forward slashes: <c>core/adt</c>. It is the <c>Path</c> column with the
        /// table's own segment dropped — the column reads <c>core\adt\EAdtConstants</c>.
        ///
        /// **This is the folder the table goes in, and the `TagTable Properties` sheet beside it
        /// is deliberately not used for that.** That sheet's own `Path` is a TIA tree folder in
        /// the maintainer's plant convention — `90_LIbrary\ADT\ADT` — which is where the table
        /// happens to sit in one project rather than where the core says it belongs. A tag table
        /// is placed by its family like every other object, or *sync folders with core* would be
        /// arguing with the importer that put it there.
        /// </summary>
        public string Folder { get; }

        /// <summary>
        /// The <c>TITLE</c> line, off the comment of the table's own row: the same JSON a block
        /// carries, so <see cref="BlockMetadata"/> reads it.
        /// </summary>
        public string Title { get; }

        /// <summary>
        /// Every user constant, **the table's own marker included**. The first row of the sheet
        /// is both: it names the table and carries its TITLE, and it is a real constant that has
        /// to be created like the rest — it is the only place a `PlcTagTable`'s metadata can
        /// live, since the type itself has a `Name` and nothing more.
        /// </summary>
        public IReadOnlyList<PlcCoreConstant> Constants { get; }

        public IReadOnlyList<PlcCoreTag> Tags { get; }

        /// <summary>Why it could not be read, or null.</summary>
        public string Problem { get; }

        public bool Read => Problem == null;

        public static ConstantsWorkbook Of(string file)
        {
            if (string.IsNullOrWhiteSpace(file)) return Failed("No workbook was named.");
            if (!File.Exists(file)) return Failed("'" + file + "' is not there.");

            try
            {
                using (FileStream stream = File.OpenRead(file))
                using (ZipArchive zip = new ZipArchive(stream, ZipArchiveMode.Read))
                {
                    IReadOnlyList<string> shared = Shared(zip);
                    IReadOnlyDictionary<string, string> sheets = Sheets(zip);

                    Sheet constants = Open(zip, sheets, ConstantsSheet, shared);

                    if (constants == null)
                        return Failed("'" + Named(file) + "' has no '" + ConstantsSheet + "' sheet.");

                    return Build(constants, Open(zip, sheets, TagsSheet, shared), Named(file));
                }
            }
            catch (Exception exception)
            {
                return Failed("'" + Named(file) + "' could not be read: " + exception.Message);
            }
        }

        // ---- Putting it together ----------------------------------------------------------------

        private static ConstantsWorkbook Build(Sheet constants, Sheet tags, string named)
        {
            foreach (string wanted in new[] { "Name", "Data Type", "Value" })
                if (!constants.Has(wanted))
                    return Failed("'" + named + "' has no '" + wanted + "' column on its '"
                                  + ConstantsSheet + "' sheet.");

            IReadOnlyList<Row> rows = constants.Rows;

            if (rows.Count == 0)
                return Failed("'" + named + "' names no tag table on its '" + ConstantsSheet + "' sheet.");

            // The first row names the table and carries the TITLE line in its comment.
            Row table = rows[0];

            string name = Trimmed(table["Name"]);

            if (name.Length == 0)
                return Failed("'" + named + "' does not name its tag table.");

            // **And it is a constant like any other, which the first version got wrong.** It
            // was read for the name and the title and then skipped, so an imported table came
            // out without the one constant that says where it came from - and a project mapped
            // afterwards could not tell a core enumeration from one of the plant's. The marker
            // is not metadata TIA keeps somewhere else: `PlcTagTable` has a `Name` and nothing
            // more, so the comment of the constant named after the table is the only place the
            // TITLE can live, which is why the workbook puts it there.
            List<PlcCoreConstant> held = new List<PlcCoreConstant>();

            for (int i = 0; i < rows.Count; i++)
                held.Add(new PlcCoreConstant(
                    Trimmed(rows[i]["Name"]),
                    Trimmed(rows[i]["Data Type"]),
                    Trimmed(rows[i]["Value"]),
                    Trimmed(rows[i]["Comment"])));

            return new ConstantsWorkbook(
                name,
                Family(table["Path"], name),
                table["Comment"],
                held,
                Listed(tags),
                null);
        }

        private static IReadOnlyList<PlcCoreTag> Listed(Sheet sheet)
        {
            if (sheet == null || !sheet.Has("Name") || !sheet.Has("Data Type")) return NoTags;

            List<PlcCoreTag> tags = new List<PlcCoreTag>();

            foreach (Row row in sheet.Rows)
                tags.Add(new PlcCoreTag(
                    Trimmed(row["Name"]),
                    Trimmed(row["Data Type"]),
                    Trimmed(row["Logical Address"]),
                    Trimmed(row["Comment"])));

            return tags;
        }

        /// <summary>
        /// <c>core\adt\EAdtConstants</c> and the table's name give <c>core/adt</c>. The last
        /// segment is dropped only when it really is the table's own name — a path that ends
        /// somewhere else is taken whole rather than trimmed on a guess.
        /// </summary>
        private static string Family(string path, string name)
        {
            string folder = Trimmed(path).Replace('\\', '/').Trim('/');

            if (folder.Length == 0) return string.Empty;

            int slash = folder.LastIndexOf('/');

            if (slash < 0)
                return string.Equals(folder, name, StringComparison.OrdinalIgnoreCase)
                    ? string.Empty : folder;

            return string.Equals(folder.Substring(slash + 1), name, StringComparison.OrdinalIgnoreCase)
                ? folder.Substring(0, slash) : folder;
        }

        // ---- The parts --------------------------------------------------------------------------

        /// <summary>
        /// Every sheet by name, pointing at the part that holds it. The name is in
        /// <c>xl/workbook.xml</c> and the part it means is behind a relationship id, so the two
        /// documents have to be read together — there is no rule tying a sheet's position to its
        /// part's file name.
        /// </summary>
        private static IReadOnlyDictionary<string, string> Sheets(ZipArchive zip)
        {
            Dictionary<string, string> sheets = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            XElement workbook = Part(zip, "xl/workbook.xml");
            XElement rels = Part(zip, "xl/_rels/workbook.xml.rels");

            if (workbook == null || rels == null) return sheets;

            XNamespace relationships = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";

            Dictionary<string, string> targets = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            foreach (XElement rel in rels.Elements())
            {
                string id = (string)rel.Attribute("Id");
                string target = (string)rel.Attribute("Target");

                if (id != null && target != null) targets[id] = target;
            }

            foreach (XElement sheet in workbook.Descendants())
            {
                if (!string.Equals(sheet.Name.LocalName, "sheet", StringComparison.Ordinal)) continue;

                string name = (string)sheet.Attribute("name");
                string id = (string)sheet.Attribute(relationships + "id");

                string target;

                if (name == null || id == null || !targets.TryGetValue(id, out target)) continue;

                if (!sheets.ContainsKey(name)) sheets.Add(name, Under(target));
            }

            return sheets;
        }

        /// <summary>A relationship target as a part path: <c>worksheets/sheet2.xml</c> is under <c>xl/</c>.</summary>
        private static string Under(string target)
        {
            string path = target.Replace('\\', '/').TrimStart('/');

            return path.StartsWith("xl/", StringComparison.OrdinalIgnoreCase) ? path : "xl/" + path;
        }

        private static Sheet Open(
            ZipArchive zip,
            IReadOnlyDictionary<string, string> sheets,
            string name,
            IReadOnlyList<string> shared)
        {
            string part;

            if (!sheets.TryGetValue(name, out part)) return null;

            XElement sheet = Part(zip, part);

            return sheet == null ? null : Sheet.Of(sheet, shared);
        }

        // ---- The shared string table ------------------------------------------------------------

        private static IReadOnlyList<string> Shared(ZipArchive zip)
        {
            List<string> shared = new List<string>();
            XElement table = Part(zip, "xl/sharedStrings.xml");

            if (table == null) return shared;

            foreach (XElement si in table.Elements())
                if (string.Equals(si.Name.LocalName, "si", StringComparison.Ordinal))
                    shared.Add(Text(si));

            return shared;
        }

        /// <summary>
        /// Every piece of text under one element, joined. **A string Excel has styled arrives as
        /// several runs**, so taking the first would truncate any value somebody made bold
        /// halfway through.
        /// </summary>
        internal static string Text(XElement element)
        {
            if (element == null) return null;

            string text = string.Empty;

            foreach (XElement run in element.Descendants())
                if (string.Equals(run.Name.LocalName, "t", StringComparison.Ordinal)) text += run.Value;

            return text;
        }

        private static XElement Part(ZipArchive zip, string path)
        {
            ZipArchiveEntry entry = zip.GetEntry(path);

            if (entry == null) return null;

            using (Stream stream = entry.Open()) return XDocument.Load(stream).Root;
        }

        private static string Named(string file)
        {
            try { return System.IO.Path.GetFileName(file); } catch { return file; }
        }

        private static string Trimmed(string value) => (value ?? string.Empty).Trim();

        private static ConstantsWorkbook Failed(string problem) =>
            new ConstantsWorkbook(null, string.Empty, null, NoConstants, NoTags, problem);

        // ---- One sheet, read by header ----------------------------------------------------------

        private sealed class Sheet
        {
            private readonly Dictionary<string, int> _headers;

            private Sheet(Dictionary<string, int> headers, IReadOnlyList<Row> rows)
            {
                _headers = headers;
                Rows = rows;
            }

            /// <summary>The rows under the header, in the order the sheet holds them.</summary>
            public IReadOnlyList<Row> Rows { get; }

            public bool Has(string header) => _headers.ContainsKey(header);

            public static Sheet Of(XElement sheet, IReadOnlyList<string> shared)
            {
                List<IReadOnlyList<string>> lines = new List<IReadOnlyList<string>>();

                foreach (XElement row in sheet.Descendants())
                    if (string.Equals(row.Name.LocalName, "row", StringComparison.Ordinal))
                        lines.Add(Cells(row, shared));

                Dictionary<string, int> headers = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

                if (lines.Count > 0)
                    for (int i = 0; i < lines[0].Count; i++)
                    {
                        string name = (lines[0][i] ?? string.Empty).Trim();

                        if (name.Length > 0 && !headers.ContainsKey(name)) headers.Add(name, i);
                    }

                List<Row> rows = new List<Row>();

                for (int i = 1; i < lines.Count; i++)
                {
                    Row row = new Row(headers, lines[i]);

                    // A blank line in the middle of a sheet is somebody's spacing, not an object.
                    if (!string.IsNullOrWhiteSpace(row["Name"])) rows.Add(row);
                }

                return new Sheet(headers, rows);
            }

            /// <summary>
            /// One row's cells, **by their own column letter rather than by how many came
            /// before**. Excel leaves an empty cell out of the file entirely, so counting
            /// elements shifts every column after the first gap.
            /// </summary>
            private static IReadOnlyList<string> Cells(XElement row, IReadOnlyList<string> shared)
            {
                List<string> cells = new List<string>();

                foreach (XElement cell in row.Elements())
                {
                    if (!string.Equals(cell.Name.LocalName, "c", StringComparison.Ordinal)) continue;

                    int at = Column((string)cell.Attribute("r"), cells.Count);

                    while (cells.Count <= at) cells.Add(null);

                    cells[at] = Value(cell, shared);
                }

                return cells;
            }

            private static string Value(XElement cell, IReadOnlyList<string> shared)
            {
                string kind = (string)cell.Attribute("t");

                if (string.Equals(kind, "inlineStr", StringComparison.Ordinal))
                    return Text(First(cell, "is"));

                XElement value = First(cell, "v");

                if (value == null) return null;

                if (!string.Equals(kind, "s", StringComparison.Ordinal)) return value.Value;

                int at;

                return int.TryParse(value.Value, out at) && at >= 0 && at < shared.Count
                    ? shared[at] : null;
            }

            private static XElement First(XElement parent, string local)
            {
                foreach (XElement child in parent.Elements())
                    if (string.Equals(child.Name.LocalName, local, StringComparison.Ordinal)) return child;

                return null;
            }

            /// <summary>A cell reference's column: <c>A1</c> is 0, <c>AB7</c> is 27.</summary>
            private static int Column(string reference, int otherwise)
            {
                if (string.IsNullOrEmpty(reference)) return otherwise;

                int at = 0;

                foreach (char letter in reference)
                {
                    char upper = char.ToUpperInvariant(letter);

                    if (upper < 'A' || upper > 'Z') break;

                    at = at * 26 + (upper - 'A' + 1);
                }

                return at > 0 ? at - 1 : otherwise;
            }
        }

        private sealed class Row
        {
            private readonly Dictionary<string, int> _headers;
            private readonly IReadOnlyList<string> _cells;

            public Row(Dictionary<string, int> headers, IReadOnlyList<string> cells)
            {
                _headers = headers;
                _cells = cells;
            }

            public string this[string header]
            {
                get
                {
                    int at;

                    return _headers.TryGetValue(header, out at) && at < _cells.Count ? _cells[at] : null;
                }
            }
        }
    }

    /// <summary>One user constant of a tag table, as the workbook writes it.</summary>
    public sealed class PlcCoreConstant
    {
        internal PlcCoreConstant(string name, string dataType, string value, string comment)
        {
            Name = name;
            DataType = dataType;
            Value = value;
            Comment = comment;
        }

        public string Name { get; }

        /// <summary>As TIA spells it — <c>Int</c>, <c>UInt</c>, <c>Word</c>.</summary>
        public string DataType { get; }

        /// <summary>As the workbook writes it, <c>16#0000</c> included: TIA parses it, not this.</summary>
        public string Value { get; }

        public string Comment { get; }

        public override string ToString() => Name + " : " + DataType + " := " + Value;
    }

    /// <summary>One PLC tag of a tag table.</summary>
    public sealed class PlcCoreTag
    {
        internal PlcCoreTag(string name, string dataType, string address, string comment)
        {
            Name = name;
            DataType = dataType;
            Address = address;
            Comment = comment;
        }

        public string Name { get; }

        public string DataType { get; }

        /// <summary>The logical address, <c>%MB0</c>. Empty for a tag that has none.</summary>
        public string Address { get; }

        public string Comment { get; }

        public override string ToString() => Name + " : " + DataType + " " + Address;
    }
}
