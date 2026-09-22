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
        /// the old one would go in. That file is in <c>repo\stranded\</c>, where
        /// <see cref="StrandedFiles"/> put it before the object was deleted, so no clean-up of
        /// the scratch folder can reach it — which is what this count used to have to guard.
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
}
