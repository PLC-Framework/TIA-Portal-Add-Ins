using System.Collections.Generic;

namespace Core.Repo
{
    /// <summary>
    /// What a move actually did, object by object.
    ///
    /// **A move is an export, a delete and an import, in that order, and the order is forced.**
    /// A block's name is unique across a PLC's software, so the copy cannot be put in place
    /// before the original is gone. That leaves one window in which the object exists only as a
    /// file - so <see cref="MovedObject.Kept"/> names that file whenever the import did not
    /// happen, and it is in <c>repo\stranded\</c>, where no run deletes it (see
    /// <see cref="StrandedFiles"/>). An object nobody can find again is the one outcome this
    /// feature must never produce.
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
}
