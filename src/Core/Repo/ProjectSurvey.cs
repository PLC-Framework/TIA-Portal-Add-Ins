using System;
using System.Collections.Generic;

namespace Core.Repo
{
    /// <summary>
    /// What a PLC holds, counted but not read: how many of each kind, and how many in each
    /// programming language.
    ///
    /// **It is the cheap half of the walk**, and that is the whole point. Both the kind and
    /// the language are typed properties in every TIA version, so this costs one pass over the
    /// tree and no exports at all - where the map itself exports every object in V17-V20.
    /// Surveying first is what lets the operator narrow a four-hundred-object PLC down to the
    /// thirty they care about before paying for any of it.
    ///
    /// **The choices come from the project, not from a list in the code.** A window offering
    /// the twenty-nine values of the language enum would be offering mostly nothing; one
    /// offering `SCL (312)` and `LAD (48)` is telling the operator what is actually there.
    /// </summary>
    public sealed class ProjectSurvey
    {
        private ProjectSurvey(IReadOnlyList<Counted> kinds, IReadOnlyList<Counted> languages, int total)
        {
            Kinds = kinds;
            Languages = languages;
            Total = total;
        }

        /// <summary>Every kind found, most numerous first.</summary>
        public IReadOnlyList<Counted> Kinds { get; }

        /// <summary>
        /// Every programming language found, most numerous first. Only the objects that have
        /// one are counted here, so the totals of the two lists do not have to match.
        /// </summary>
        public IReadOnlyList<Counted> Languages { get; }

        public int Total { get; }

        public static ProjectSurvey Of(IDictionary<string, int> kinds, IDictionary<string, int> languages, int total) =>
            new ProjectSurvey(Ordered(kinds), Ordered(languages), total);

        /// <summary>
        /// Most numerous first, then by name. **Ordered here rather than in the window**, so
        /// two windows cannot disagree about it, and so the thing an operator is most likely
        /// to want to untick is the one at the front.
        /// </summary>
        private static IReadOnlyList<Counted> Ordered(IDictionary<string, int> counted)
        {
            List<Counted> found = new List<Counted>();

            if (counted != null)
                foreach (KeyValuePair<string, int> one in counted)
                    found.Add(new Counted(one.Key, one.Value));

            found.Sort((left, right) =>
            {
                int byCount = right.Count.CompareTo(left.Count);

                return byCount != 0 ? byCount : string.Compare(left.Name, right.Name, StringComparison.Ordinal);
            });

            return found;
        }
    }

    /// <summary>One name and how many of it there are.</summary>
    public sealed class Counted
    {
        public Counted(string name, int count)
        {
            Name = name;
            Count = count;
        }

        public string Name { get; }

        public int Count { get; }

        /// <summary>
        /// What a tick box shows: <c>SCL (312)</c>. **`ToString` and not a template binding**,
        /// because a bound object's `ToString` is also what a screen reader and a test read -
        /// the trap `GroupNode` and `DataBlockItem` both hit before this.
        /// </summary>
        public override string ToString() => Name + " (" + Count + ")";
    }
}
