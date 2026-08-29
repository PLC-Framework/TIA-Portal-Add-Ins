using System.Collections.Generic;
using System.Runtime.Serialization;

namespace core.config
{
    [DataContract]
    public class Group
    {
        [DataMember(Name = "name")]
        public string Name { get; set; }

        [DataMember(Name = "groups")]
        public List<Group> Groups { get; set; }
    }
}
