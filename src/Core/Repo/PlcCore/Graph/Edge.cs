using System.Collections.Generic;
using System.Runtime.Serialization;

namespace Core.Repo.PlcCore.Graph
{
    /// <summary>
    /// One declared dependency. An edge only ever takes one of three shapes, and
    /// the generator omits the keys that do not belong to the shape at hand:
    ///
    ///     resolved   Resolved = true,  Ambiguous = false
    ///     ambiguous  Resolved = false, Ambiguous = true,  Candidates set
    ///     external   Resolved = false, Ambiguous = false, External = true, Kind set
    ///
    /// Branch on those flags rather than on which fields happen to be null.
    /// </summary>
    [DataContract]
    public class Edge
    {
        [DataMember(Name = "from")]
        public string From { get; set; }

        /// <summary>Target id when resolved, otherwise the raw dependency name.</summary>
        [DataMember(Name = "to")]
        public string To { get; set; }

        [DataMember(Name = "resolved")]
        public bool Resolved { get; set; }

        [DataMember(Name = "ambiguous")]
        public bool Ambiguous { get; set; }

        /// <summary>Ambiguous edges only.</summary>
        [DataMember(Name = "candidates")]
        public List<string> Candidates { get; set; }

        /// <summary>External edges only; always true when present.</summary>
        [DataMember(Name = "external")]
        public bool External { get; set; }

        /// <summary>External edges only. system | untracked | unknown</summary>
        [DataMember(Name = "kind")]
        public string Kind { get; set; }
    }
}
