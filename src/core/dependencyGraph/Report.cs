using System.Collections.Generic;
using System.Runtime.Serialization;

namespace core.dependencyGraph
{
    /// <summary>
    /// One diagnostic about the graph. Level, Type and Message are always present;
    /// the rest depend on Type, and the generator omits the ones that do not apply:
    ///
    ///     ambiguous-dependency, unknown-dependency,
    ///     system-dependency, untracked-dependency  ->  Dependency + UsedBy
    ///     name-mismatch                            ->  File + Base + Name
    ///     broken-deprecation                       ->  File + DeprecatedBy
    ///
    /// Branch on Type rather than on which fields happen to be null.
    /// </summary>
    [DataContract]
    public class Report
    {
        /// <summary>error | warning | info</summary>
        [DataMember(Name = "level")]
        public string Level { get; set; }

        /// <summary>
        /// ambiguous-dependency | broken-deprecation | name-mismatch |
        /// unknown-dependency | system-dependency | untracked-dependency
        /// </summary>
        [DataMember(Name = "type")]
        public string Type { get; set; }

        [DataMember(Name = "message")]
        public string Message { get; set; }

        /// <summary>Dependency-related types only.</summary>
        [DataMember(Name = "dependency")]
        public string Dependency { get; set; }

        /// <summary>Dependency-related types only.</summary>
        [DataMember(Name = "usedBy")]
        public List<string> UsedBy { get; set; }

        /// <summary>name-mismatch and broken-deprecation only.</summary>
        [DataMember(Name = "file")]
        public string File { get; set; }

        /// <summary>name-mismatch only.</summary>
        [DataMember(Name = "base")]
        public string Base { get; set; }

        /// <summary>name-mismatch only.</summary>
        [DataMember(Name = "name")]
        public string Name { get; set; }

        /// <summary>broken-deprecation only.</summary>
        [DataMember(Name = "deprecatedBy")]
        public string DeprecatedBy { get; set; }
    }
}
