using System.Collections.Generic;
using System.Runtime.Serialization;

namespace Core.Repo.PlcCore.Graph
{
    /// <summary>
    /// One diagnostic about the graph. Level, Type and Message are always present;
    /// the rest depend on Type, and the generator omits the ones that do not apply:
    ///
    ///     ambiguous-dependency, unknown-dependency,
    ///     system-dependency, untracked-dependency  ->  Dependency + UsedBy
    ///     name-mismatch                            ->  File + Base + Name
    ///     version-mismatch                         ->  File + Version + Expected
    ///     header-mismatch                          ->  File + Attribute + Found + Expected
    ///     broken-deprecation                       ->  File + DeprecatedBy
    ///     missing-version, missing-title,
    ///     interface-unreadable                     ->  File
    ///
    /// Branch on Type rather than on which fields happen to be null: on a
    /// header-mismatch, Found or Expected is null precisely when one side is absent.
    /// </summary>
    [DataContract]
    public class Report
    {
        /// <summary>error | warning | info</summary>
        [DataMember(Name = "level")]
        public string Level { get; set; }

        /// <summary>
        /// ambiguous-dependency | broken-deprecation | name-mismatch | version-mismatch |
        /// missing-version | missing-title | header-mismatch | unknown-dependency |
        /// system-dependency | untracked-dependency | interface-unreadable
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

        /// <summary>Every type that is about one source file.</summary>
        [DataMember(Name = "file")]
        public string File { get; set; }

        /// <summary>name-mismatch only.</summary>
        [DataMember(Name = "base")]
        public string Base { get; set; }

        /// <summary>name-mismatch only.</summary>
        [DataMember(Name = "name")]
        public string Name { get; set; }

        /// <summary>version-mismatch only: the version the metadata declares, "v1.1".</summary>
        [DataMember(Name = "version")]
        public string Version { get; set; }

        /// <summary>
        /// version-mismatch and header-mismatch: what the file had to say - the file
        /// name's version, or the TITLE's author or family. Null on a header-mismatch
        /// when the TITLE declares no such key.
        /// </summary>
        [DataMember(Name = "expected")]
        public string Expected { get; set; }

        /// <summary>header-mismatch only: VERSION, AUTHOR or FAMILY.</summary>
        [DataMember(Name = "attribute")]
        public string Attribute { get; set; }

        /// <summary>header-mismatch only: what the .scl header says, or null when it has no such line.</summary>
        [DataMember(Name = "found")]
        public string Found { get; set; }

        /// <summary>broken-deprecation only.</summary>
        [DataMember(Name = "deprecatedBy")]
        public string DeprecatedBy { get; set; }
    }
}
