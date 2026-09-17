using System.Collections.Generic;

namespace Core.Repo
{
    /// <summary>
    /// What *sync folders with core* would move: every object that comes from the core and is
    /// not in the folder its family names.
    ///
    /// **It is the comparison's `Misplaced` finding and nothing else.** The window already shows
    /// those rows and already says what is wrong with each; working the list out a second way
    /// would be two answers to one question, and the first time they disagreed the button would
    /// move something the panel above it called correct.
    ///
    /// **Pure, and made before anything is touched**, like <see cref="DownloadPlan"/>: what the
    /// operator is shown and what the move then does come from one answer.
    ///
    /// **It names a family, never a TIA folder.** `core/adt/queue` is the core's own word for
    /// where a block belongs; which tree that sits under - `Program blocks`, `PLC data types` -
    /// is TIA's, it follows the interface language, and putting it together here would be a
    /// Siemens name inside a binary. The adapter starts at the root the object's kind decides.
    /// </summary>
    public sealed class SyncPlan
    {
        private static readonly MisplacedObject[] None = new MisplacedObject[0];

        private SyncPlan(IReadOnlyList<MisplacedObject> objects)
        {
            Objects = objects;
        }

        public IReadOnlyList<MisplacedObject> Objects { get; }

        public int Count => Objects.Count;

        public static SyncPlan Of(CoreComparison comparison)
        {
            if (comparison == null) return new SyncPlan(None);

            List<MisplacedObject> found = new List<MisplacedObject>();

            foreach (ComparedObject one in comparison.Objects)
            {
                if (!one.Is(Finding.Misplaced) || one.Found == null) continue;

                // Misplaced is only ever set on an object whose family is known, but the guard
                // stays: a move with nowhere to move to would delete and then have nothing to
                // import into, which is the one failure here that cannot be undone.
                if (string.IsNullOrWhiteSpace(one.Expected)) continue;

                found.Add(new MisplacedObject(
                    one.Found.Name, one.Found.Kind, one.Found.Folder, one.Expected.Trim()));
            }

            return new SyncPlan(found);
        }
    }

    /// <summary>One object to move, and where from and to.</summary>
    public sealed class MisplacedObject
    {
        internal MisplacedObject(string name, string kind, string from, string family)
        {
            Name = name;
            Kind = kind;
            From = from;
            Family = family;
        }

        /// <summary>
        /// The object's own name. **This is what finds it**, not the folder: a block's name is
        /// unique across a PLC's software, so a walk by name is exact where a path would have to
        /// be taken apart into a tree root whose name follows the interface language.
        /// </summary>
        public string Name { get; }

        /// <summary>As the map spells it - <c>FB</c>, <c>GlobalDB</c>, <c>PlcStruct</c>, <c>PlcTagTable</c>.</summary>
        public string Kind { get; }

        /// <summary>Where it is now, as the map wrote it. For the report; nothing resolves it.</summary>
        public string From { get; }

        /// <summary>Where the core says it belongs: <c>core/adt/queue</c>.</summary>
        public string Family { get; }

        public override string ToString() => Name + ": " + From + " -> " + Family;
    }

    /// <summary>
    /// What a move actually did, object by object.
    ///
    /// **A move is an export, a delete and an import, in that order, and the order is forced.**
    /// A block's name is unique across a PLC's software, so the copy cannot be put in place
    /// before the original is gone. That leaves one window in which the object exists only as a
    /// file - so <see cref="MovedObject.Kept"/> names that file whenever the import did not
    /// happen, and the run does not delete it. An object nobody can find again is the one
    /// outcome this feature must never produce.
    /// </summary>
    public sealed class SyncReport
    {
        private readonly List<MovedObject> _results = new List<MovedObject>();
        private readonly List<string> _problems = new List<string>();

        public IReadOnlyList<MovedObject> Results => _results;

        /// <summary>What went wrong for the run rather than for one object.</summary>
        public IReadOnlyList<string> Problems => _problems;

        public int Moved => Counted(true);

        public int Failed => Counted(false);

        /// <summary>How many objects are on disk as a file and no longer in the project.</summary>
        public int Stranded
        {
            get
            {
                int found = 0;

                foreach (MovedObject one in _results)
                    if (!string.IsNullOrEmpty(one.Kept)) found++;

                return found;
            }
        }

        public void Add(MovedObject result) => _results.Add(result);

        public void Add(string problem)
        {
            if (!string.IsNullOrWhiteSpace(problem)) _problems.Add(problem);
        }

        private int Counted(bool done)
        {
            int found = 0;

            foreach (MovedObject one in _results)
                if (one.Done == done) found++;

            return found;
        }
    }

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
