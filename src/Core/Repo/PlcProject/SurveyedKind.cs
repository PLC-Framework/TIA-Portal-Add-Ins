using System.Collections.Generic;

namespace Core.Repo.PlcProject
{
    /// <summary>One kind the project holds, and what languages it is written in.</summary>
    public sealed class SurveyedKind
    {
        public SurveyedKind(string name, int count, IReadOnlyList<Counted> languages)
        {
            Name = name;
            Count = count;
            Languages = languages ?? new List<Counted>();
        }

        /// <summary>As <c>Core.Config.CodingStyleNames</c> spells it.</summary>
        public string Name { get; }

        public int Count { get; }

        /// <summary>
        /// The languages found inside this kind, most numerous first. **Empty is an ordinary
        /// answer**: a PLC data type and a tag table have no programming language at all.
        /// </summary>
        public IReadOnlyList<Counted> Languages { get; }

        public override string ToString() => Name + " (" + Count + ")";
    }
}
