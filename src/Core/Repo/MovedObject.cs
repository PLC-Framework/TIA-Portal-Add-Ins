namespace Core.Repo
{
    /// <summary>One object a move attempted, and how it went.</summary>
    public sealed class MovedObject
    {
        private MovedObject(MisplacedObject planned, bool done, string problem, string kept)
        {
            Planned = planned;
            Done = done;
            Problem = problem;
            Kept = kept;
        }

        public MisplacedObject Planned { get; }

        public bool Done { get; }

        /// <summary>Why it did not move, in TIA's own words where TIA gave any.</summary>
        public string Problem { get; }

        /// <summary>
        /// The file the object was exported to, when it is no longer in the project and did not
        /// go back in. Null on every other outcome, including a refusal before the delete.
        /// </summary>
        public string Kept { get; }

        public string Name => Planned.Name;

        public static MovedObject Went(MisplacedObject planned) =>
            new MovedObject(planned, true, null, null);

        /// <summary>It would not move, and it is still where it was.</summary>
        public static MovedObject Refused(MisplacedObject planned, string problem) =>
            new MovedObject(planned, false, problem ?? "TIA Portal refused it without saying why.", null);

        /// <summary>
        /// It came out and would not go back in. **The file is kept and named**, because the
        /// object is not in the project any more and that sentence is the only way back.
        /// </summary>
        public static MovedObject Stranded(MisplacedObject planned, string problem, string file) =>
            new MovedObject(
                planned,
                false,
                (problem ?? "TIA Portal refused it without saying why.") +
                " It is no longer in the project; its export is at " + file,
                file);
    }
}
