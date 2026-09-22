using System.Collections.Generic;
using System.Runtime.Serialization;

namespace Core.Checks
{
    /// <summary>One rule as it stood when the check ran.</summary>
    [DataContract]
    public sealed class ReportRule
    {
        public const string ObjectCatalogue = "object";
        public const string InterfaceCatalogue = "interface";

        [DataMember(Name = "id", Order = 0)]
        public string Id { get; set; }

        /// <summary><c>object</c> or <c>interface</c>: which catalogue the id came from.</summary>
        [DataMember(Name = "catalogue", Order = 1)]
        public string Catalogue { get; set; }

        [DataMember(Name = "regex", Order = 2)]
        public string Regex { get; set; }

        [DataMember(Name = "descriptions", Order = 3)]
        public List<string> Descriptions { get; set; }
    }
}
