using System.Collections.Generic;
using System.Runtime.Serialization;

namespace Core.Repo.PlcProject
{
    /// <summary>
    /// One kind the map was asked for, and the languages wanted inside it.
    ///
    /// **Public with public setters, like everything a serializer touches here** - the rule
    /// partial trust taught this project once, kept even where the writer runs in full trust.
    /// </summary>
    [DataContract]
    public sealed class KindFilter
    {
        /// <summary>
        /// As <c>Core.Config.CodingStyleNames</c> spells it - <c>OB</c>, <c>FB</c>, <c>FC</c>,
        /// <c>GlobalDB</c>, <c>PlcStruct</c>, <c>PlcTagTable</c> and the rest.
        /// </summary>
        [DataMember(Name = "kind", Order = 0)]
        public string Kind { get; set; }

        /// <summary>
        /// The languages wanted inside this kind, as the Openness enum names them - <c>SCL</c>,
        /// <c>LAD</c>, <c>GRAPH</c>, <c>DB</c>. **Empty or absent means the whole kind**, which
        /// is also what a kind with no language at all looks like: a PLC data type and a tag
        /// table have none.
        /// </summary>
        [DataMember(Name = "languages", Order = 1)]
        public List<string> Languages { get; set; }

        public static KindFilter Of(string kind, IEnumerable<string> languages)
        {
            List<string> wanted = new List<string>();

            if (languages != null)
                foreach (string one in languages)
                    if (!string.IsNullOrEmpty(one)) wanted.Add(one);

            return new KindFilter { Kind = kind, Languages = wanted.Count == 0 ? null : wanted };
        }
    }
}
