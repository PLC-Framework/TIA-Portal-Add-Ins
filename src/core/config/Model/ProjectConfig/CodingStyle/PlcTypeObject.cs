using System.Collections.Generic;
using System.Runtime.Serialization;

namespace Core.Config
{
    [DataContract]
    public class PlcTypeObject
    {
        [DataMember(Name = "type")]
        public string Type { get; set; }

        [DataMember(Name = "implements")]
        public List<string> Implements { get; set; }
    }
}
