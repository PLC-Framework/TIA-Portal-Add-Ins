using System.Collections.Generic;
using System.Runtime.Serialization;

namespace Core.Repo.PlcProject
{
    /// <summary>One kind the project holds, and what languages it is written in.</summary>
    [DataContract]
    public sealed class SurveyedKind
    {
        public SurveyedKind()
        {
            Languages = new List<Counted>();
        }

        public SurveyedKind(string name, int count, List<Counted> languages)
        {
            Name = name;
            Count = count;
            Languages = languages ?? new List<Counted>();
        }

        /// <summary>As <c>Core.Config.CodingStyleNames</c> spells it.</summary>
        [DataMember(Name = "name", Order = 0)]
        public string Name { get; set; }

        [DataMember(Name = "count", Order = 1)]
        public int Count { get; set; }

        /// <summary>
        /// The languages found inside this kind, most numerous first. **Empty is an ordinary
        /// answer**: a PLC data type and a tag table have no programming language at all.
        /// </summary>
        [DataMember(Name = "languages", Order = 2)]
        public List<Counted> Languages { get; set; }

        public override string ToString() => Name + " (" + Count + ")";
    }
}
