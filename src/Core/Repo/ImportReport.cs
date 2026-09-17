using System.Collections.Generic;

namespace Core.Repo
{
    /// <summary>
    /// What a download actually did, object by object.
    ///
    /// **Nothing is rolled back and nothing pretends to be.** Openness has no transaction, so a
    /// run that fails half way has already written half of it; the honest answer is to carry on,
    /// name every refusal, and leave a report somebody can act on — the same rule the export
    /// already follows. Undoing what went in would mean deleting blocks the operator asked for
    /// because a later one refused, which is a worse outcome than a project that says where it
    /// stopped.
    /// </summary>
    public sealed class ImportReport
    {
        private readonly List<ImportedNode> _results = new List<ImportedNode>();
        private readonly List<string> _problems = new List<string>();

        /// <summary>One entry per planned node, in the order they were attempted.</summary>
        public IReadOnlyList<ImportedNode> Results => _results;

        /// <summary>
        /// What went wrong for the run as a whole rather than for one object — nowhere to write,
        /// a PLC that is no longer there.
        /// </summary>
        public IReadOnlyList<string> Problems => _problems;

        public int Imported => Counted(true);

        public int Failed => Counted(false);

        public int Skipped
        {
            get
            {
                int found = 0;

                foreach (ImportedNode one in _results)
                    if (one.Planned.Action == DownloadAction.Skip) found++;

                return found;
            }
        }

        public void Add(ImportedNode result) => _results.Add(result);

        public void Add(string problem)
        {
            if (!string.IsNullOrWhiteSpace(problem)) _problems.Add(problem);
        }

        private int Counted(bool done)
        {
            int found = 0;

            foreach (ImportedNode one in _results)
                if (one.Planned.Action != DownloadAction.Skip && one.Done == done) found++;

            return found;
        }
    }

    /// <summary>One object a download attempted, and how it went.</summary>
    public sealed class ImportedNode
    {
        private ImportedNode(PlannedNode planned, bool done, string problem)
        {
            Planned = planned;
            Done = done;
            Problem = problem;
        }

        public PlannedNode Planned { get; }

        public bool Done { get; }

        /// <summary>Why it did not go in, in TIA's own words where TIA gave any.</summary>
        public string Problem { get; }

        public string Name => Planned.Node.Base;

        public static ImportedNode Went(PlannedNode planned) => new ImportedNode(planned, true, null);

        public static ImportedNode Refused(PlannedNode planned, string problem) =>
            new ImportedNode(planned, false, problem ?? "TIA Portal refused it without saying why.");

        /// <summary>
        /// A dependency already at the version the core stands behind. **Reported rather than
        /// left out**, so the count of what a download touched can be read against what it was
        /// asked for.
        /// </summary>
        public static ImportedNode Left(PlannedNode planned) => new ImportedNode(planned, true, null);
    }
}
