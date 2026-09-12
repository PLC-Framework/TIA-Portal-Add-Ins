using System.Collections.Generic;
using System.Runtime.Serialization;

namespace Core.Config
{
    /// <summary>
    /// A TIA object type, and the object rules its names may answer to.
    ///
    /// The rules are **alternatives**: a name passes if it matches any one of them. An FC
    /// offering <c>function</c>, <c>safety_function</c>, <c>subroutine</c> and
    /// <c>safety_subroutine</c> is offering four spellings, and no name could satisfy two
    /// at once.
    /// </summary>
    [DataContract]
    public class PlcTypeObject
    {
        [DataMember(Name = "type")]
        public string Type { get; set; }

        [DataMember(Name = "implements")]
        public List<string> Implements { get; set; }
    }
}
