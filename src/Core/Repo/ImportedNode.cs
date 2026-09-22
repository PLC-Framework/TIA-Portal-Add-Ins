namespace Core.Repo
{
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
