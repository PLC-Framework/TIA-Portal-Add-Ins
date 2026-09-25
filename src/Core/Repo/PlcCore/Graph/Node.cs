using System.Collections.Generic;
using System.Runtime.Serialization;

namespace Core.Repo.PlcCore.Graph
{
    /// <summary>
    /// One .scl or .udt file in the graph. Every key is always present in core.json,
    /// though Version, DeprecatedBy and Interface may be null.
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

        /// <summary>
        /// How an FB or FC is called, or null: for a UDT, for a constant table, for a block
        /// whose interface the generator could not read (it says so in <c>reports</c> as
        /// <c>interface-unreadable</c>), and for every node of a <c>core.json</c> generated
        /// before 2026-09-25, which has no such key - so a caller can tell "this block takes no
        /// parameters" (empty lists) from "nothing is known" (null).
        /// </summary>
        [DataMember(Name = "interface")]
        public BlockInterface Interface { get; set; }
    }
}
