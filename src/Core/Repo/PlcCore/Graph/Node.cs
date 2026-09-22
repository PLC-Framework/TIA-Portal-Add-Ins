using System.Collections.Generic;
using System.Runtime.Serialization;

namespace Core.Repo.PlcCore.Graph
{
    /// <summary>
    /// One .scl or .udt file in the graph. Every key is always present in core.json,
    /// though Version and DeprecatedBy may be null.
    /// </summary>
    [DataContract]
    public class Node
    {
        /// <summary>File name without extension; unique within a family.</summary>
        [DataMember(Name = "id")]
        public string Id { get; set; }

        /// <summary>TIA symbol declared on the first line of the file.</summary>
        [DataMember(Name = "name")]
        public string Name { get; set; }

        /// <summary>Id without the -vX.Y suffix; unversioned dependencies resolve against this.</summary>
        [DataMember(Name = "base")]
        public string Base { get; set; }

        /// <summary>"1.0", with no leading "v". Null when the name carries no suffix.</summary>
        [DataMember(Name = "version")]
        public string Version { get; set; }

        /// <summary>
        /// current | deprecated. Taken verbatim from a hand-written TITLE, so treat
        /// anything outside those two as unknown rather than assuming.
        /// </summary>
        [DataMember(Name = "status")]
        public string Status { get; set; }

        /// <summary>Id of the replacement file. Null unless deprecated.</summary>
        [DataMember(Name = "deprecatedBy")]
        public string DeprecatedBy { get; set; }

        /// <summary>Forward-slash path, relative to the generator's working directory.</summary>
        [DataMember(Name = "file")]
        public string File { get; set; }

        /// <summary>Raw names as written in TITLE, before resolution.</summary>
        [DataMember(Name = "dependencies")]
        public List<string> Dependencies { get; set; }
    }
}
