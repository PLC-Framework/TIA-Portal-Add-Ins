using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using Core.Checks;

namespace Satellite.CodingStyleReport.Report
{
    /// <summary>One report row, as the table shows it.</summary>
    public sealed class ReportLine
    {
        /// <summary>How many description lines a suggested rule shows in the tooltip.</summary>
        private const int SuggestionLines = 1;

        public ReportLine(ReportRow row, IReadOnlyDictionary<string, ReportRule> rules)
        {
            Row = row;
            RulesTip = BuildRulesTip(row, rules);
            SearchText = string.Join("\n", Row.Plc, Row.Unit, Row.Owner, Row.Kind, Row.Name, Row.Path, Rules, Row.Note);
        }

        public ReportRow Row { get; }

        /// <summary>The outcome as the enum; null for a name this window does not know.</summary>
        public CheckOutcome? Outcome => Row.OutcomeValue;

        /// <summary>The outcome's name, what the row's colour triggers on.</summary>
        public string OutcomeName => Row.Outcome;

        public string Result => Outcomes.Label(Row);

        public string Plc => Row.Plc;

        /// <summary>The software unit, or the star the report writes for the general program.</summary>
        public string Unit => Row.Unit;

        /// <summary>
        /// The object this row is part of - and, on an object's own row, itself. Which tag
        /// table a constant belongs to is the first question a member row raises, and the
        /// row above it only answers that while nothing is filtered or sorted.
        /// </summary>
        public string Owner => Row.Owner;

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
        /// The rules behind the Rules cell, as the report carried them: the ones matched in full,
        /// the suggestions by pattern and first line. Null when there are none, so no empty
        /// tooltip opens.
        ///
        /// Suggestions are shortened on purpose. A failing FC is offered nine rules; nine full
        /// descriptions is a tooltip taller than the screen, and what the operator needs from a
        /// suggestion is which one it was aiming at, not the whole of each.
        /// </summary>
        public string RulesTip { get; }

        // Tooltips for cells that may be cut short. Null rather than empty: an empty string still
        // opens a tooltip, as a small blank box over a row that has nothing to say.
        public string NameTip => Blank(Row.Name);
        public string OwnerTip => Blank(Row.Owner);
        public string PathTip => Blank(Row.Path);
        public string NoteTip => Blank(Row.Note);

        private static string Blank(string value) => string.IsNullOrEmpty(value) ? null : value;

        /// <summary>Everything the search box looks through, joined once rather than per keystroke.</summary>
        public string SearchText { get; }

        private static string BuildRulesTip(ReportRow row, IReadOnlyDictionary<string, ReportRule> rules)
        {
            bool matched = row.Matched.Count > 0;
            IReadOnlyList<string> ids = matched ? row.Matched : row.Suggestions;
            if (ids.Count == 0) return null;

            StringBuilder text = new StringBuilder();

            foreach (string id in ids)
            {
                if (text.Length > 0) text.Append('\n').Append('\n');

                if (rules == null || !rules.TryGetValue(id, out ReportRule rule))
                {
                    // A report from somewhere that did not carry its rules: say so rather than
                    // show nothing, which would read as a rule with no description.
                    text.Append(id).Append("  (not described in this report)");
                    continue;
                }

                text.Append(rule.Id).Append("  (").Append(rule.Catalogue).Append(" rule)").Append('\n');
                text.Append(rule.Regex);

                IEnumerable<string> lines = matched
                    ? rule.Descriptions
                    : rule.Descriptions.Take(SuggestionLines);

                foreach (string line in lines) text.Append('\n').Append(line);
            }

            return text.ToString();
        }

        /// <summary>
        /// The name, not the type. A ListViewItem takes its automation name from this, so without
        /// it every row announces itself as Satellite.CodingStyleReport.Report.ReportLine - to a
        /// screen reader as much as to a test. The same trap the other two windows hit.
        /// </summary>
        public override string ToString() => Row.Name ?? string.Empty;
    }
}
