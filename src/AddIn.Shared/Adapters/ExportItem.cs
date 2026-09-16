using System;

namespace AddIn.Shared.Adapters
{
    /// <summary>
    /// One object the Add-In can write to disk: where it lives in the project, and how to
    /// write it once somebody has decided which folder it goes in.
    ///
    /// **The walk finds it, the action places it, the delegate writes it.** Splitting it that
    /// way is what keeps the tree's shape in `Core.Exports.ExportTree` - a decision the
    /// report and the export must agree on - while the only thing that touches Siemens stays
    /// inside the version project.
    ///
    /// **An object is not one file.** TIA offers a different set of formats per programming
    /// language - SimaticML for everything, a source for SCL, STL, DB and UDT, SIMATIC SD for
    /// most of them - so the item is handed a folder and a base name and writes what its own
    /// object supports.
    /// </summary>
    public sealed class ExportItem
    {
        private readonly Func<string, string, ExportOutcome> _write;

        public ExportItem(string plc, string unit, string folders, string name, Func<string, string, ExportOutcome> write)
        {
            Plc = plc ?? string.Empty;
            Unit = unit ?? string.Empty;
            Folders = folders ?? string.Empty;
            Name = name ?? string.Empty;
            _write = write;
        }

        public string Plc { get; }

        /// <summary>Its software unit, empty for the general program.</summary>
        public string Unit { get; }

        /// <summary>The folders it sits in, inside the PLC or the unit: <c>Program blocks/03-ALL</c>.</summary>
        public string Folders { get; }

        public string Name { get; }

        /// <summary>
        /// Writes every format this object has into that folder, under that base name.
        /// **Never throws**: one object that will not come out is not a reason to end an
        /// export of several thousand.
        /// </summary>
        /// <param name="name">The file name without an extension, already safe for the file system.</param>
        public ExportOutcome Write(string folder, string name)
        {
            try
            {
                return _write == null
                    ? ExportOutcome.Refused("There is nothing to export it with.")
                    : _write(folder, name);
            }
            catch (Exception exception)
            {
                return ExportOutcome.Refused(exception.Message);
            }
        }
    }

    /// <summary>
    /// What came of writing one object: how many files, and what did not come out.
    ///
    /// **Partly is its own answer**, and that is the point of the type. A block whose
    /// SimaticML was written and whose source TIA refused is neither a success nor a failure,
    /// and reporting it as either would mislead somebody reading the export as a backup.
    /// </summary>
    public sealed class ExportOutcome
    {
        private ExportOutcome(int files, string problem)
        {
            Files = files;
            Problem = problem;
        }

        /// <summary>How many files were written.</summary>
        public int Files { get; }

        /// <summary>What did not come out, naming the formats. Null when everything did.</summary>
        public string Problem { get; }

        /// <summary>Nothing at all came out. Named <c>Is</c>- so the factory below can keep the verb.</summary>
        public bool IsRefused => Files == 0;

        public static ExportOutcome Written(int files) => new ExportOutcome(files, null);

        public static ExportOutcome Partly(int files, string problem) =>
            files == 0 ? Refused(problem) : new ExportOutcome(files, problem);

        public static ExportOutcome Refused(string problem) =>
            new ExportOutcome(0, problem ?? "It could not be exported.");
    }
}
