using System;

namespace AddIn.Shared.Adapters
{
    /// <summary>
    /// One object the Add-In can write to disk: where it lives in the project, and how to
    /// export it once somebody has decided where it goes.
    ///
    /// **The walk finds it, the action places it, the delegate writes it.** Splitting it that
    /// way is what keeps the tree's shape in `Core.Exports.ExportTree` - a decision the
    /// report and the export must agree on - while the only thing that touches Siemens stays
    /// inside the version project.
    /// </summary>
    public sealed class ExportItem
    {
        private readonly Func<string, string> _exportTo;

        public ExportItem(string plc, string unit, string folders, string name, Func<string, string> exportTo)
        {
            Plc = plc ?? string.Empty;
            Unit = unit ?? string.Empty;
            Folders = folders ?? string.Empty;
            Name = name ?? string.Empty;
            _exportTo = exportTo;
        }

        public string Plc { get; }

        /// <summary>Its software unit, empty for the general program.</summary>
        public string Unit { get; }

        /// <summary>The folders it sits in, inside the PLC or the unit: <c>Program blocks/03-ALL</c>.</summary>
        public string Folders { get; }

        public string Name { get; }

        /// <summary>
        /// Writes it to that path. Returns null when it was written, or the reason it was not
        /// - a block TIA refuses to export because it is not consistent, a know-how protected
        /// one, a folder that could not be made. **Never throws**: one object that will not
        /// come out is not a reason to end an export of several thousand.
        /// </summary>
        public string ExportTo(string path)
        {
            try
            {
                return _exportTo == null ? "There is nothing to export it with." : _exportTo(path);
            }
            catch (Exception exception)
            {
                return exception.Message;
            }
        }
    }
}
