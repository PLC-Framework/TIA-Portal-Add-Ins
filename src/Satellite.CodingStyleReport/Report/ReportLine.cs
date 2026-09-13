using Core.Checks;

namespace Satellite.CodingStyleReport.Report
{
    /// <summary>One report row, as the table shows it.</summary>
    public sealed class ReportLine
    {
        public ReportLine(ReportRow row)
        {
            Row = row;
        }

        public ReportRow Row { get; }

        public string Result
        {
            get
            {
                switch (Row.OutcomeValue)
                {
                    case CheckOutcome.Passed: return "Passed";
                    case CheckOutcome.Failed: return "Failed";
                    case CheckOutcome.NotConfigured: return "Not configured";
                    case CheckOutcome.Skipped: return "Skipped";
                    default: return Row.Outcome;
                }
            }
        }

        public string Kind => Row.Kind;

        /// <summary>A member is indented under the object it belongs to, which is the row above it.</summary>
        public string Name => Row.IsMember ? "    " + Row.Name : Row.Name;

        public string Path => Row.Path;

        /// <summary>What it matched; failing that, what it was probably aiming at.</summary>
        public string Rules =>
            Row.Matched.Count > 0
                ? string.Join(", ", Row.Matched)
                : Row.Suggestions.Count > 0 ? "try: " + string.Join(", ", Row.Suggestions) : string.Empty;

        public string Note => Row.Note;

        /// <summary>
        /// The name, not the type. A ListViewItem takes its automation name from this, so without
        /// it every row announces itself as Satellite.CodingStyleReport.Report.ReportLine - to a
        /// screen reader as much as to a test. The same trap the other two windows hit.
        /// </summary>
        public override string ToString() => Row.Name ?? string.Empty;
    }
}
