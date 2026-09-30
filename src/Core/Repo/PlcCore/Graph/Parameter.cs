namespace Core.Repo.PlcCore.Graph
{
    /// <summary>
    /// One parameter of a core FB's or FC's call interface: its name, and its type as the
    /// source declares it. Mirrors the generator's <c>Parameter</c> in
    /// <c>code/tools/dependency_graph_builder/models/parameter.py</c>.
    ///
    /// **The type is TIA's own spelling** - <c>Bool</c>, <c>Time</c>, <c>Array[0..9] of Byte</c>,
    /// and a PLC data type in its quotes, <c>"delayOnOff"</c> - since the core's sources are
    /// TIA's own exports, and it is what a call in SimaticML writes in its
    /// <c>&lt;Parameter Type="..."&gt;</c>. **A call cannot do without it**: TIA refuses to import
    /// one that leaves it out, measured on the VM on 2026-09-30. A parameter declared
    /// <c>Struct</c> is typed <c>Struct</c>, its members not listed.
    /// </summary>
    public class Parameter
    {
        public string Name { get; set; }

        /// <summary>
        /// Null when not known - which is what every parameter of a <c>core.json</c> written
        /// before 2026-09-30 reads back as, that file listing names alone.
        /// </summary>
        public string Type { get; set; }

        public override string ToString() => Type == null ? Name : Name + " : " + Type;
    }
}
