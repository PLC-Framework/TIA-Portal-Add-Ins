using System.Collections.Generic;
using System.Runtime.Serialization;

namespace Core.Repo.PlcProject
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
        /// What this document's shape is, so a reader meeting another one says so rather than
        /// showing half of it. Same reason `StyleReport` carries one.
        ///
        /// **3** (2026-09-23): `counts` carries what the walk found in the whole scope, so the
        /// window can offer the filter without walking the project first. **2** (2026-09-17):
        /// `filter` carries one entry per kind, each naming the languages wanted inside it,
        /// where format 1 had two flat lists. An older map is refused rather than half-read -
        /// its filter names members this version cannot see, and reading it as though it
        /// covered the whole PLC would be exactly the silent hole this document exists not to
        /// have. That costs one click: unlike a coding-style report, this file is a snapshot of
        /// the project right now and is rebuilt by pressing *Load*.
        /// </summary>
        public const int CurrentFormat = 3;

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

        /// <summary>
        /// What this map was asked to cover, when it was asked to cover less than everything.
        ///
        /// **A filtered map is not a map of the project**, and saying so is the difference
        /// between "the project has no FCs" and "this map did not look for them". Null when
        /// nothing was narrowed.
        /// </summary>
        [DataMember(Name = "filter", Order = 5)]
        public MapFilter Filter { get; set; }

        /// <summary>
        /// How many of each kind the walk **visited**, and in which languages - whatever the
        /// filter then kept.
        ///
        /// **Here rather than walked for again**: the window builds its filter boxes from this,
        /// so opening it on a project that has been mapped once costs no pass over the tree at
        /// all. And because it counts what was visited rather than what was mapped, a narrowed
        /// map still says what the whole scope holds - a filter that only ever narrowed would
        /// forget what it had left out.
        /// </summary>
        [DataMember(Name = "counts", Order = 6)]
        public ProjectSurvey Counts { get; set; }

        [DataMember(Name = "objects", Order = 7)]
        public List<ProjectObject> Objects { get; set; }

        /// <summary>
        /// What could not be read while walking - a folder that refused, a title that would
        /// not come back. **Kept rather than thrown**, so a map with holes says where they
        /// are instead of looking complete.
        /// </summary>
        [DataMember(Name = "problems", Order = 8)]
        public List<string> Problems { get; set; }

        public static ProjectMap Of(string project, string plc, string unit, string builtAt) =>
            new ProjectMap
            {
                Format = CurrentFormat,
                BuiltAt = builtAt,
                Project = project,
                Plc = plc,
                Unit = Places.UnitOrGeneral(unit),
                Counts = ProjectSurvey.Empty,
                Objects = new List<ProjectObject>(),
                Problems = new List<string>()
            };
    }
}
