using System.Collections.Generic;
using System.Runtime.Serialization;

namespace core.config
{
    [DataContract]
    public class CodingStyle
    {
        [DataMember(Name = "rules")]
        public List<Rule> Rules { get; set; }

        [DataMember(Name = "blocks")]
        public List<PlcTypeObject> Blocks { get; set; }

        [DataMember(Name = "technologyObjects")]
        public List<PlcTypeObject> TechnologyObjects { get; set; }

        [DataMember(Name = "tagTables")]
        public List<PlcTypeObject> TagTables { get; set; }

        [DataMember(Name = "types")]
        public List<PlcTypeObject> Types { get; set; }

        [DataMember(Name = "alarmTextLists")]
        public List<PlcTypeObject> AlarmTextLists { get; set; }
    }
}
