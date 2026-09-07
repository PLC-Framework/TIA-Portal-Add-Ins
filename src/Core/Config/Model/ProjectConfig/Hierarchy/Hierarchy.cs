using System.Collections.Generic;
using System.Runtime.Serialization;

namespace Core.Config
{
    [DataContract]
    public class Hierarchy
    {
        [DataMember(Name = "blocks")]
        public List<Group> Blocks { get; set; }

        [DataMember(Name = "technologyObjects")]
        public List<Group> TechnologyObjects { get; set; }

        [DataMember(Name = "tagTables")]
        public List<Group> TagTables { get; set; }

        [DataMember(Name = "types")]
        public List<Group> Types { get; set; }

        /// <summary>
        /// Applied inside every existing software unit. Only S7-1500 has them; on an
        /// S7-1200 there are none and this section is simply ignored.
        /// </summary>
        [DataMember(Name = "softwareUnits")]
        public SoftwareUnitHierarchy SoftwareUnits { get; set; }
    }
}
