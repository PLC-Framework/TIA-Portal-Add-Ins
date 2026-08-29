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

    }
}
