using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;

namespace Core.Checks
{
    /// <summary>
    /// A <see cref="CheckRow"/> in a form that survives serialization, partial trust and a
    /// spreadsheet: enums as their names, lists never null.
    /// </summary>
    [DataContract]
    public sealed class ReportRow
    {
        /// <summary>
        /// What <see cref="Unit"/> says when the row belongs to the general program.
        ///
        /// Part of this document's published surface, so it stays - but it is
        /// <see cref="Places.GeneralProgram"/>'s value rather than its own literal, because
        /// the core updater takes the same character as an argument and two spellings in two
        /// assemblies would drift with nothing failing to compile.
        /// </summary>
        public const string GeneralProgram = Places.GeneralProgram;

        /// <summary><c>Object</c> or <c>Member</c>, the names of <see cref="RowScope"/>.</summary>
        [DataMember(Name = "scope", Order = 0)]
        public string Scope { get; set; }

        /// <summary><c>Passed</c>, <c>Failed</c>, <c>NotConfigured</c> or <c>Skipped</c>: the names of <see cref="CheckOutcome"/>.</summary>
        [DataMember(Name = "outcome", Order = 1)]
        public string Outcome { get; set; }

        [DataMember(Name = "plc", Order = 2)]
        public string Plc { get; set; }

        /// <summary>
        /// The software unit, or <see cref="GeneralProgram"/> for the program itself.
        ///
        /// **The report writes the star, the checker does not.** Inside the check "no unit"
        /// is simply an empty string; saying so in one character is how this document
        /// presents it, and a reader that meets an empty cell knows it came from a report
        /// written before this field existed rather than from the general program.
        /// </summary>
        [DataMember(Name = "unit", Order = 3)]
        public string Unit { get; set; }

        /// <summary>The object the row belongs to; on an object's own row, itself.</summary>
        [DataMember(Name = "object", Order = 4)]
        public string Owner { get; set; }

        [DataMember(Name = "kind", Order = 5)]
        public string Kind { get; set; }

        /// <summary>The members this one is declared inside, joined with dots; empty at the top of a section.</summary>
        [DataMember(Name = "parent", Order = 6)]
        public string Parent { get; set; }

        [DataMember(Name = "name", Order = 7)]
        public string Name { get; set; }

        [DataMember(Name = "path", Order = 8)]
        public string Path { get; set; }

        [DataMember(Name = "matched", Order = 9)]
        public List<string> Matched { get; set; }

        [DataMember(Name = "suggestions", Order = 10)]
        public List<string> Suggestions { get; set; }

        [DataMember(Name = "note", Order = 11)]
        public string Note { get; set; }

        /// <summary>The outcome as the enum, or null when the text names none - a report from elsewhere.</summary>
        public CheckOutcome? OutcomeValue =>
            Enum.TryParse(Outcome, false, out CheckOutcome value) ? value : (CheckOutcome?)null;

        public bool IsMember => string.Equals(Scope, RowScope.Member.ToString(), StringComparison.Ordinal);

        internal static ReportRow From(CheckRow row) => new ReportRow
        {
            Scope = row.Scope.ToString(),
            Outcome = row.Outcome.ToString(),
            Plc = row.Plc,
            Unit = string.IsNullOrEmpty(row.Unit) ? GeneralProgram : row.Unit,
            Owner = row.Owner,
            Kind = row.Kind,
            Parent = row.Parent,
            Name = row.Name,
            Path = row.Path,
            Matched = row.Matched.ToList(),
            Suggestions = row.Suggestions.ToList(),
            Note = row.Note
        };

        internal void Normalise()
        {
            Matched = Matched ?? new List<string>();
            Suggestions = Suggestions ?? new List<string>();
        }
    }
}
