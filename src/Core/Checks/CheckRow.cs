using System.Collections.Generic;

namespace Core.Checks
{
    public enum CheckOutcome
    {
        /// <summary>The name matched at least one of the rules offered to it.</summary>
        Passed,

        /// <summary>It matched none of them.</summary>
        Failed,

        /// <summary>
        /// Nothing in the configuration covers this type, so there was nothing to check it
        /// against. Reported rather than skipped: a type nobody configured is a hole in the
        /// coding style, and a report that silently omits it looks like a clean result.
        /// </summary>
        NotConfigured,

        /// <summary>
        /// Not checked, and the note says why - a rule whose pattern will not compile or
        /// times out, or a member of an object whose own name matched nothing, so no
        /// interface is known.
        /// </summary>
        Skipped
    }

    /// <summary>Whether a row is about an object or about something inside one.</summary>
    public enum RowScope
    {
        Object,
        Member
    }

    /// <summary>
    /// One line of the report. Objects and members share the shape on purpose: the report is
    /// a table somebody sorts, filters and exports, and one shape is what lets a member row
    /// sit beside the object row it belongs to without a second set of columns.
    /// </summary>
    public sealed class CheckRow
    {
        public CheckRow(
            RowScope scope,
            CheckOutcome outcome,
            string plc,
            string unit,
            string owner,
            string kind,
            string parent,
            string name,
            string path,
            IReadOnlyList<string> matched = null,
            IReadOnlyList<string> suggestions = null,
            string note = null)
        {
            Scope = scope;
            Outcome = outcome;
            Plc = plc ?? string.Empty;
            Unit = unit ?? string.Empty;
            Owner = owner ?? string.Empty;
            Kind = kind;
            Parent = parent ?? string.Empty;
            Name = name;
            Path = path ?? string.Empty;
            Matched = matched ?? new List<string>();
            Suggestions = suggestions ?? new List<string>();
            Note = note;
        }

        public RowScope Scope { get; }

        public CheckOutcome Outcome { get; }

        /// <summary>The PLC the row is about.</summary>
        public string Plc { get; }

        /// <summary>Its software unit, empty for the general program.</summary>
        public string Unit { get; }

        /// <summary>
        /// The object this row belongs to: the tag table a constant is in, the block a member
        /// is in - and, on an object's own row, itself.
        ///
        /// **Filled on both kinds of row on purpose.** A member row's owner is the only thing
        /// that says what it is part of once the table is sorted or filtered, and repeating
        /// the object's own name here is what makes "show me everything of this table" one
        /// filter rather than two.
        /// </summary>
        public string Owner { get; }

        /// <summary>
        /// TIA's type for an object row - <c>FB</c>, <c>GlobalDB</c> - and the interface
        /// section for a member row: <c>Static</c>, <c>Tag</c>. One column, because to the
        /// reader both answer the same question about the line in front of them.
        /// </summary>
        public string Kind { get; }

        /// <summary>
        /// For a member row, the members it is declared inside, joined with dots - empty at
        /// the top of a section and on an object's own row. A name is checked on its own, so
        /// this is what says where it lives without joining the two into something no rule
        /// could read.
        /// </summary>
        public string Parent { get; }

        public string Name { get; }

        /// <summary>
        /// The folders it lives in, inside its PLC or unit, so a failing name can be found
        /// without searching for it. A member row carries its object's folders: the object
        /// itself is in <see cref="Owner"/>.
        /// </summary>
        public string Path { get; }

        /// <summary>The rules the name satisfied. Any one of them is enough to pass.</summary>
        public IReadOnlyList<string> Matched { get; }

        /// <summary>
        /// The rules it was offered and did not satisfy. On a failing row these are what it
        /// was most likely aiming at, which is more use than repeating that it failed.
        /// </summary>
        public IReadOnlyList<string> Suggestions { get; }

        /// <summary>Why a row is NotConfigured or Skipped. Null when the outcome speaks for itself.</summary>
        public string Note { get; }
    }
}
