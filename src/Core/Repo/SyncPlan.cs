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

        public static SyncPlan Of(PlcCoreComparison comparison)
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
}
