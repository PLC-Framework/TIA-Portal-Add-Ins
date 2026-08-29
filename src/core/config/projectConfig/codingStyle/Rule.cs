using System.Collections.Generic;
using System.Runtime.Serialization;

namespace Core.Config
{
    [DataContract]
    public class Rule
    {
        [DataMember(Name = "id")]
        public string Id { get; set; }

        [DataMember(Name = "regex")]
        public string Regex { get; set; }

        [DataMember(Name = "descriptions")]
        public List<string> Descriptions { get; set; }
    }
}
