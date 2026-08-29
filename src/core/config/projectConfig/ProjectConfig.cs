using System.Runtime.Serialization;

namespace core.config
{
    [DataContract]
    public class ProjectConfig
    {
        [DataMember(Name = "hierarchy")]
        public Hierarchy Hierarchy { get; set; }

        [DataMember(Name = "codingStyle")]
        public CodingStyle CodingStyle { get; set; }
    }
}