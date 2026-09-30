using System.Collections.Generic;

namespace Core.Repo.PlcCore.Graph
{
    /// <summary>
    /// How an FB or FC of the core is called: the parameters of each section, in the order the
    /// block declares them, and an FC's return type. Mirrors the generator's own
    /// <c>BlockInterface</c> in <c>code/tools/dependency_graph_builder/models/interface.py</c>.
    ///
    /// **Each parameter carries its type, and the first version did not** (2026-09-25, names
    /// only). The reasoning was that TIA takes a parameter's type from the block being called;
    /// the VM said otherwise on 2026-09-30 - a LAD call whose <c>&lt;Parameter&gt;</c> has no
    /// <c>Type</c> is refused on import - and a template that only names a core function has to
    /// write the whole call out of this. **Only the top level**: a parameter declared
    /// <c>Struct</c> is one parameter to a caller.
    ///
    /// **Read from the generator, never from the source here.** The generator already opens
    /// every source for its TITLE, and one reader of SCL in one place is what keeps two
    /// languages from disagreeing about the same file.
    ///
    /// **Not a data contract, because the file has carried two shapes**: a parameter is
    /// <c>{"name", "type"}</c> now and was a bare name before, and the serializer cannot read
    /// one list that could hold either. <see cref="PlcCoreInterfaceReader"/> fills it.
    /// </summary>
    public class BlockInterface
    {
        /// <summary><c>VAR_INPUT</c>, in declaration order.</summary>
        public List<Parameter> Input { get; set; }

        /// <summary><c>VAR_OUTPUT</c>, in declaration order.</summary>
        public List<Parameter> Output { get; set; }

        /// <summary><c>VAR_IN_OUT</c>, in declaration order.</summary>
        public List<Parameter> InOut { get; set; }

        /// <summary>An FC's return type as written on its first line - <c>Int</c>, <c>Void</c> - or null for an FB.</summary>
        public string Return { get; set; }
    }
}
