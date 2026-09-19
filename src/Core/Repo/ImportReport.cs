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

        /// <summary>How many were moved into the folder their family names on the way in.</summary>
        public int Moved
        {
            get
            {
                int found = 0;

                foreach (ImportedNode one in _results)
                    if (one.MovedFrom != null) found++;

                return found;
            }
        }

        /// <summary>
        /// How many exist only as a file: taken out to make room, and neither the new one nor
        /// the old one would go in. **Whatever runs the import must keep the scratch folder when
        /// this is not zero** — clearing it would throw the only copy away.
        /// </summary>
        public int Stranded
        {
            get
            {
                int found = 0;

                foreach (ImportedNode one in _results)
                    if (one.File != null) found++;

                return found;
            }
        }

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

        /// <summary>
        /// Why it did not go in, in TIA's own words where TIA gave any — or, on one that did,
        /// what is still not as asked.
        /// </summary>
        public string Problem { get; }

        /// <summary>The folder it was moved out of on the way in, or null when it was not moved.</summary>
        public string MovedFrom { get; private set; }

        /// <summary>
        /// Where the only copy of it now is, when it was taken out to make room and would not go
        /// back in. Null for everything else.
        /// </summary>
        public string File { get; private set; }

        public string Name => Planned.Node.Base;

        public static ImportedNode Went(PlannedNode planned) => new ImportedNode(planned, true, null);

        /// <summary>
        /// Written, **and moved out of <paramref name="from"/> into the folder its family names**.
        /// TIA overwrites an object where it is, so this is the one that had to be taken out and
        /// put in again.
        /// </summary>
        public static ImportedNode Moved(PlannedNode planned, string from) =>
            new ImportedNode(planned, true, null) { MovedFrom = string.IsNullOrEmpty(from) ? "another folder" : from };

        /// <summary>
        /// Written, but **left where it was rather than moved**, with the reason. It is the
        /// core's object now; only its folder is not the family's, which is what the next
        /// comparison will say and what *sync folders with core* is for.
        /// </summary>
        public static ImportedNode InPlace(PlannedNode planned, string why) =>
            new ImportedNode(planned, true, why);

        public static ImportedNode Refused(PlannedNode planned, string problem) =>
            new ImportedNode(planned, false, problem ?? "TIA Portal refused it without saying why.");

        /// <summary>
        /// **Neither the new one nor the old one is in the project**, and the old one is a file
        /// at <paramref name="file"/>. The one outcome a download must never hide: an object
        /// nobody can find again.
        /// </summary>
        public static ImportedNode Stranded(PlannedNode planned, string problem, string file) =>
            new ImportedNode(planned, false, problem) { File = file };

        /// <summary>
        /// Left as it is — a dependency already at the version the core stands behind, or one
        /// the operator unticked. **Reported rather than left out**, so the count of what a
        /// download touched can be read against what it was asked for.
        /// </summary>
        public static ImportedNode Left(PlannedNode planned) => new ImportedNode(planned, true, null);
    }
}
