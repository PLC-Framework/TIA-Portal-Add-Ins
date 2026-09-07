using System.Collections.Generic;
using System.Runtime.Serialization;

namespace Core.Config
{
    /// <summary>
    /// Group structure to create inside every software unit.
    ///
    /// One declaration applies to all units: they are named by the user according to the
    /// plant's architecture, so the config describes what goes *inside* a unit, never
    /// which units exist. Nothing here creates units.
    ///
    /// There is no technologyObjects entry: a software unit exposes blocks, tag tables
    /// and types only.
    /// </summary>
    [DataContract]
    public class SoftwareUnitHierarchy
    {
        [DataMember(Name = "blocks")]
        public List<Group> Blocks { get; set; }

        [DataMember(Name = "tagTables")]
        public List<Group> TagTables { get; set; }

        [DataMember(Name = "types")]
        public List<Group> Types { get; set; }
    }
}
