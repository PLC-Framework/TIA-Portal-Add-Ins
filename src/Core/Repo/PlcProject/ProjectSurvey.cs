using System;
using System.Collections.Generic;
using System.Runtime.Serialization;

using Core.Config;

namespace Core.Repo.PlcProject
{
    /// <summary>
    /// What a PLC holds, counted but not read: how many of each kind, and within each kind how
    /// many in each programming language.
    ///
    /// **It is the cheap half of the walk**, and that is the whole point. Both the kind and the
    /// language are typed properties in every TIA version, so counting costs nothing - where
    /// *reading* an object is one export in V17-V20. Counting is what lets the operator narrow a
    /// four-hundred-object PLC down to the thirty they care about before paying for any of it.
    ///
    /// **A language is counted inside its kind, not beside it** (2026-09-17). Two flat lists
    /// could say the PLC holds 40 SCL objects and 31 FBs, and never which of the two the SCL
    /// was in - so the window could only offer a filter that meant "SCL everywhere", which is
    /// not a question anybody asks of a project.
    ///
    /// **The choices come from the project, not from a list in the code.** A window offering
    /// the twenty-nine values of the language enum would be offering mostly nothing; one
    /// offering `SCL (312)` under FC is telling the operator what is actually there. The day a
    /// GRAPH block arrives it appears on its own row with its own count, and nothing here
    /// changes.
    ///
    /// **It is counted inside the map's own walk and travels in `repo\project.json`**
    /// (2026-09-23, the maintainer's decision). It used to be a walk of its own, run when the
    /// window opened and on every scope change - which is a second pass over the tree for
    /// numbers the map's pass could produce for nothing, since the walk reaches every object
    /// anyway and only the *reading* of one costs anything in V17-V20. Written into the map,
    /// the boxes are also there the next time the window opens, with no walk at all.
    ///
    /// **It counts what the walk visited, not what the filter kept**, which is what makes the
    /// boxes describe the scope rather than the last narrowing of it - otherwise a filter could
    /// only ever be made smaller, each run forgetting what it had left out.
    ///
    /// **Serialized, so public setters and a parameterless constructor**, exactly as `MapFilter`
    /// beside it: it is a document member now, whatever else it is.
    /// </summary>
    [DataContract]
    public sealed class ProjectSurvey
    {
        /// <summary>
        /// The order a PLC reads in, which is the maintainer's: code blocks, then data blocks,
        /// then the types and tables underneath them.
        ///
        /// **Ordered here rather than by count**, which is what this used to do. Most-numerous-
        /// first moves a row every time a project changes, and the operator is looking for a
        /// row by name - "the FCs" - not for the biggest one. Anything this list has never
        /// heard of follows, by name, so a kind added to the object model shows up rather than
        /// disappearing.
        /// </summary>
        private static readonly string[] Order =
        {
            CodingStyleNames.OB,
            CodingStyleNames.FB,
            CodingStyleNames.FC,
            CodingStyleNames.GlobalDB,
            CodingStyleNames.InstanceDB,
            CodingStyleNames.ArrayDB,
            CodingStyleNames.TechnologicalInstanceDB,
            CodingStyleNames.PlcStruct,
            CodingStyleNames.PlcTagTable
        };

        public ProjectSurvey()
        {
            Kinds = new List<SurveyedKind>();
        }

        private ProjectSurvey(List<SurveyedKind> kinds, int total)
        {
            Kinds = kinds;
            Total = total;
        }

        /// <summary>Every kind found, in <see cref="Order"/>.</summary>
        [DataMember(Name = "kinds", Order = 0)]
        public List<SurveyedKind> Kinds { get; set; }

        [DataMember(Name = "total", Order = 1)]
        public int Total { get; set; }

        /// <summary>A survey that counts nothing, for a scope with nothing in it.</summary>
        public static ProjectSurvey Empty => new ProjectSurvey(new List<SurveyedKind>(), 0);

        /// <summary>
        /// Counts objects as a walk reaches them.
        ///
        /// **Core does the counting so that two adapters cannot disagree about it**, which is
        /// the same reason the walk itself is written once per version and no more. The adapter
        /// knows how to reach an object; how many of them there are is not a TIA question.
        /// </summary>
        public static Builder Building() => new Builder();

        public sealed class Builder
        {
            private readonly Dictionary<string, Dictionary<string, int>> _languages =
                new Dictionary<string, Dictionary<string, int>>(StringComparer.OrdinalIgnoreCase);

            private readonly Dictionary<string, int> _counts =
                new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            private int _total;

            /// <param name="language">
            /// Null or empty for a kind that has none - a PLC data type, a tag table. **Not
            /// counted as a language**, rather than counted under an empty name: there would be
            /// no such box to tick, and the row would claim a language it has not got.
            /// </param>
            public void Found(string kind, string language)
            {
                if (string.IsNullOrEmpty(kind)) return;

                _total++;

                int count;
                _counts[kind] = _counts.TryGetValue(kind, out count) ? count + 1 : 1;

                if (string.IsNullOrEmpty(language)) return;

                Dictionary<string, int> inside;

                if (!_languages.TryGetValue(kind, out inside))
                {
                    inside = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                    _languages[kind] = inside;
                }

                int found;
                inside[language] = inside.TryGetValue(language, out found) ? found + 1 : 1;
            }

            public ProjectSurvey Done()
            {
                List<SurveyedKind> kinds = new List<SurveyedKind>();

                foreach (KeyValuePair<string, int> one in _counts)
                {
                    Dictionary<string, int> inside;

                    kinds.Add(new SurveyedKind(
                        one.Key,
                        one.Value,
                        _languages.TryGetValue(one.Key, out inside) ? Ordered(inside) : new List<Counted>()));
                }

                kinds.Sort((left, right) =>
                {
                    int byOrder = Rank(left.Name).CompareTo(Rank(right.Name));

                    return byOrder != 0
                        ? byOrder
                        : string.Compare(left.Name, right.Name, StringComparison.Ordinal);
                });

                return new ProjectSurvey(kinds, _total);
            }

            /// <summary>Where a kind sits in <see cref="Order"/>, or after everything it holds.</summary>
            private static int Rank(string kind)
            {
                for (int i = 0; i < Order.Length; i++)
                    if (string.Equals(Order[i], kind, StringComparison.OrdinalIgnoreCase)) return i;

                return Order.Length;
            }

            /// <summary>
            /// The languages inside one kind, most numerous first, then by name. **Ordered by
            /// count here where the kinds are not**, and the difference is what the operator is
            /// doing: a kind is looked up by name, a language inside it is scanned to see what
            /// this row is mostly made of.
            /// </summary>
            private static List<Counted> Ordered(Dictionary<string, int> counted)
            {
                List<Counted> found = new List<Counted>();

                foreach (KeyValuePair<string, int> one in counted) found.Add(new Counted(one.Key, one.Value));

                found.Sort((left, right) =>
                {
                    int byCount = right.Count.CompareTo(left.Count);

                    return byCount != 0 ? byCount : string.Compare(left.Name, right.Name, StringComparison.Ordinal);
                });

                return found;
            }
        }
    }
}
