using System;
using System.Collections.Generic;
using System.Runtime.Serialization;

namespace Core.Repo
{
    /// <summary>
    /// What a TIA project actually holds, as the core updater read it - the other half of
    /// every comparison, and what `repo\project.json` carries.
    ///
    /// **Everything is mapped, not only what looks like the core**, and the maintainer asked
    /// for it in those terms: a block can have been downloaded into the wrong folder, or a
    /// folder somebody made by hand can hold core blocks. Without the whole picture only one
    /// of the three discrepancies is visible - wrong version - and the other two, *here and it
    /// should not be* and *here but in the wrong place*, need the objects the core has never
    /// heard of as much as the ones it has.
    ///
    /// **Every type in it is public with public setters**, like `StyleReport`: it is
    /// serialized, and this project has already paid once for a type a serializer could not
    /// see.
    /// </summary>
    [DataContract]
    public sealed class ProjectMap
    {
        /// <summary>
        /// What this document's shape is, so a reader meeting a newer one says so rather than
        /// showing half of it. Same reason `StyleReport` carries one.
        /// </summary>
        public const int CurrentFormat = 1;

        [DataMember(Name = "format", Order = 0)]
        public int Format { get; set; }

        /// <summary>ISO-8601 with an offset, as a string - see `DependencyGraph.GeneratedAt`.</summary>
        [DataMember(Name = "builtAt", Order = 1)]
        public string BuiltAt { get; set; }

        [DataMember(Name = "project", Order = 2)]
        public string Project { get; set; }

        [DataMember(Name = "plc", Order = 3)]
        public string Plc { get; set; }

        /// <summary>
        /// The software unit this covers, or <see cref="Places.GeneralProgram"/> for the PLC's
        /// own program. **One scope per map**, because that is what the operator chose.
        /// </summary>
        [DataMember(Name = "unit", Order = 4)]
        public string Unit { get; set; }

        [DataMember(Name = "objects", Order = 5)]
        public List<ProjectObject> Objects { get; set; }

        /// <summary>
        /// What could not be read while walking - a folder that refused, a title that would
        /// not come back. **Kept rather than thrown**, so a map with holes says where they
        /// are instead of looking complete.
        /// </summary>
        [DataMember(Name = "problems", Order = 6)]
        public List<string> Problems { get; set; }

        public static ProjectMap Of(string project, string plc, string unit, string builtAt) =>
            new ProjectMap
            {
                Format = CurrentFormat,
                BuiltAt = builtAt,
                Project = project,
                Plc = plc,
                Unit = Places.UnitOrGeneral(unit),
                Objects = new List<ProjectObject>(),
                Problems = new List<string>()
            };
    }

    /// <summary>One thing the project holds, with whatever it says about itself.</summary>
    [DataContract]
    public sealed class ProjectObject
    {
        [DataMember(Name = "name", Order = 0)]
        public string Name { get; set; }

        /// <summary>
        /// <c>OB</c>, <c>FC</c>, <c>FB</c>, <c>GlobalDB</c>, <c>InstanceDB</c>, <c>ArrayDB</c>,
        /// <c>TechnologicalInstanceDB</c>, <c>PlcStruct</c> or <c>PlcTagTable</c> - the names
        /// `Core.Config.CodingStyleNames` already uses, so one vocabulary serves both.
        /// </summary>
        [DataMember(Name = "kind", Order = 1)]
        public string Kind { get; set; }

        /// <summary>Where it sits, as folders inside the PLC or unit: <c>Program blocks/03-ALL</c>.</summary>
        [DataMember(Name = "folder", Order = 2)]
        public string Folder { get; set; }

        /// <summary>
        /// The version its TITLE declares, without the leading <c>v</c>, or null when it
        /// carries no metadata - **which is what "not from the core" means here**.
        /// </summary>
        [DataMember(Name = "version", Order = 3)]
        public string Version { get; set; }

        [DataMember(Name = "status", Order = 4)]
        public string Status { get; set; }

        [DataMember(Name = "deprecatedBy", Order = 5)]
        public string DeprecatedBy { get; set; }

        /// <summary>
        /// The folder its TITLE says it belongs in, <c>core/adt/queue</c>. Held against
        /// <see cref="Folder"/> this is what catches a block downloaded into the wrong place -
        /// without the repository having to be reachable.
        /// </summary>
        [DataMember(Name = "family", Order = 6)]
        public string Family { get; set; }

        [DataMember(Name = "dependencies", Order = 7)]
        public List<string> Dependencies { get; set; }

        /// <summary>
        /// TIA's own <c>VERSION</c> header, which a block carries beside its TITLE and a PLC
        /// data type does not have at all.
        ///
        /// **Recorded because the two can disagree**, and the repository has been seen to do
        /// it the other way round - a file named `-v1.1` whose TITLE said `v1.0`. Which of the
        /// two a comparison believes is stage four's to decide; losing one of them here would
        /// take the decision away from it.
        /// </summary>
        [DataMember(Name = "headerVersion", Order = 8)]
        public string HeaderVersion { get; set; }

        /// <summary>
        /// TIA's own <c>FAMILY</c> header, which the core writes as <c>core/adt/queue</c> -
        /// the same value its TITLE carries, in a field the object model exposes directly.
        ///
        /// **It is the safety net for V20**, whose `PlcBlock` has no `Title` property at all;
        /// version and family are the two fields a comparison cannot do without, and both are
        /// native here. A block with no TITLE and a `core/` family is still recognisably from
        /// the core.
        /// </summary>
        [DataMember(Name = "headerFamily", Order = 9)]
        public string HeaderFamily { get; set; }

        /// <summary>
        /// Why this object's metadata could not be read, or null. A title that meant to be
        /// metadata and is not parseable lands here rather than being dropped.
        /// </summary>
        [DataMember(Name = "problem", Order = 10)]
        public string Problem { get; set; }

        /// <summary>
        /// Whether it claims to come from the core at all.
        ///
        /// **A TITLE is the answer where there is one, and the native header is the answer
        /// where there is not** - which is not a fallback for tidiness but the only reading
        /// available in V20, where a block has no `Title` property. A `FAMILY` of
        /// <c>core/…</c> is the core's own convention, written by whoever wrote the block.
        /// </summary>
        public bool FromCore =>
            !string.IsNullOrEmpty(Version) ||
            (HeaderFamily != null && HeaderFamily.StartsWith("core/", StringComparison.OrdinalIgnoreCase));
    }
}
