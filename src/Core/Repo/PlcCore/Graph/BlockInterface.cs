using System.Collections.Generic;
using System.Runtime.Serialization;

namespace Core.Repo.PlcCore.Graph
{
    /// <summary>
    /// How an FB or FC of the core is called: the parameter names of each section, in the
    /// order the block declares them, and an FC's return type. Mirrors the generator's own
    /// <c>BlockInterface</c> in <c>code/tools/dependency_graph_builder/models/interface.py</c>.
    ///
    /// **Names only, on purpose** (the maintainer's decision, 2026-09-25). Writing a call needs
    /// a parameter's name and its section - <c>:=</c> for an input or an in-out, <c>=&gt;</c> for
    /// an output - and TIA takes each parameter's type from the block being called; the
    /// SimaticML schema for a LAD call leaves <c>Type</c> optional as well. **Only the top
    /// level**: a parameter declared <c>Struct</c> is one name to a caller.
    ///
    /// **Read from the generator, never from the source here.** The generator already opens
    /// every source for its TITLE, and one reader of SCL in one place is what keeps two
    /// languages from disagreeing about the same file - which the TITLE reader once had to be
    /// measured against, all 249 sources of it.
    /// </summary>
    [DataContract]
    public class BlockInterface
    {
        /// <summary>Names in <c>VAR_INPUT</c>, in declaration order.</summary>
        [DataMember(Name = "input")]
        public List<string> Input { get; set; }

        /// <summary>Names in <c>VAR_OUTPUT</c>, in declaration order.</summary>
        [DataMember(Name = "output")]
        public List<string> Output { get; set; }

        /// <summary>Names in <c>VAR_IN_OUT</c>, in declaration order.</summary>
        [DataMember(Name = "inout")]
        public List<string> InOut { get; set; }

        /// <summary>An FC's return type as written on its first line - <c>Int</c>, <c>Void</c> - or null for an FB.</summary>
        [DataMember(Name = "return")]
        public string Return { get; set; }
    }
}
