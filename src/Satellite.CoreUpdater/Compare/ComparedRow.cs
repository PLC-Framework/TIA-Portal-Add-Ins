using System.Collections.Generic;
using System.Windows.Media;

using Core.Repo;

namespace Satellite.CoreUpdater.Compare
{
    /// <summary>
    /// One row of the project panel, as the table shows it.
    ///
    /// **The words live here, not in the window**, for the reason `ReportLine` and `Outcomes`
    /// already record one satellite over: what a finding is called is read in two places - the
    /// table and the tick box that filters it - and two spellings of "outdated" is how a filter
    /// stops matching the rows it names.
    /// </summary>
    public sealed class ComparedRow
    {
        private ComparedRow(ComparedObject source, string says, Brush ink)
        {
            Source = source;
            Says = says;
            Ink = ink;
        }

        /// <summary>What the comparison said, kept whole so a later stage can act on the row.</summary>
        public ComparedObject Source { get; }

        public string Name => Source.Found?.Name;

        public string Kind => Source.Found?.Kind;

        public string Version => Source.Version;

        public string Folder => Source.Found?.Folder;

        /// <summary>Every finding on this row, in one cell.</summary>
        public string Says { get; }

        /// <summary>
        /// The same words as a tooltip, since that cell is the one the width runs out on - and
        /// **null rather than empty on a clean row**, because an empty tooltip is a small blank
        /// box hanging off the pointer. The trap the coding-style report already records.
        /// </summary>
        public string SaysTip => string.IsNullOrEmpty(Says) ? null : Says;

        /// <summary>
        /// What colour the row is painted, bound through `ItemContainerStyle`. **A Brush rather
        /// than a name**, because a binding cannot resolve a resource key and the window is the
        /// half that has the dictionary.
        /// </summary>
        public Brush Ink { get; }

        public static ComparedRow Of(ComparedObject source, Brush ink) =>
            new ComparedRow(source, Words(source), ink);

        /// <summary>
        /// What a line says about one object: its findings, or **"up to date" when it has none**.
        ///
        /// Every line says something, which is what makes the two panels read alike — the
        /// repository side always has a state to give, and a core block with nothing against it
        /// would otherwise be the one line on either side that trailed off into nothing.
        /// </summary>
        public static string Message(ComparedObject one) => one.Clean ? "up to date" : Words(one);

        /// <summary>
        /// The findings of one object, spelled out. Empty for a row with nothing against it -
        /// **not "ok"**, which would be a word competing with the five that mean something.
        /// </summary>
        public static string Words(ComparedObject one)
        {
            List<string> said = new List<string>();

            foreach (Finding finding in one.Findings) said.Add(Word(one, finding));

            return string.Join(", ", said);
        }

        private static string Word(ComparedObject one, Finding finding)
        {
            switch (finding)
            {
                case Finding.Outdated:
                    return one.Replacement == null
                        ? "outdated"
                        : "outdated — the core has v" + one.Replacement.Version;

                case Finding.UnknownVersion:
                    return one.Versions.Count == 0
                        ? "the core does not define this name"
                        : "the core has " + Versions(one);

                case Finding.Misplaced:
                    return "belongs in " + one.Expected;

                case Finding.Disagrees:
                    return "TITLE says v" + one.Found.Version + ", the header says v" + one.Found.HeaderVersion;

                case Finding.NotFromCore:
                    return "not from the core";
            }

            return finding.ToString();
        }

        private static string Versions(ComparedObject one)
        {
            List<string> versions = new List<string>();

            foreach (Core.DependencyGraph.Node node in one.Versions)
                if (CoreStatus.IsCurrent(node.Status)) versions.Add("v" + node.Version);

            // Every version is retired, which is a real answer rather than an empty sentence:
            // the core still knows the name and stands behind none of them.
            if (versions.Count == 0) return "no version of it current";

            return string.Join(", ", versions);
        }

        /// <summary>
        /// Which named ink a row is painted in, **most serious first**: a block that is both
        /// retired and misplaced is a red row, because that is the one to look at.
        /// </summary>
        public static string InkKey(ComparedObject one)
        {
            if (one.Is(Finding.Outdated) || one.Is(Finding.UnknownVersion)) return "InkBad";
            if (one.Is(Finding.Misplaced) || one.Is(Finding.Disagrees)) return "InkWarn";
            if (one.Is(Finding.NotFromCore)) return "InkMuted";

            return "InkGood";
        }

        /// <summary>
        /// The name, which a `ListViewItem` also takes as its **automation name** - the trap
        /// `GroupNode`, `DataBlockItem` and `Counted` each hit before this one.
        /// </summary>
        public override string ToString() => Name;
    }
}
